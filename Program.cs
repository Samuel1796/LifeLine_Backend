using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using Nook.Api.Data;
using Nook.Api.Hubs;
using Nook.Api.Services;

// Load KEY=VALUE pairs from a local .env file (kept out of source control)
// into environment variables before configuration is built.
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
        var separator = trimmed.IndexOf('=');
        if (separator <= 0) continue;
        Environment.SetEnvironmentVariable(trimmed[..separator].Trim(), trimmed[(separator + 1)..].Trim());
    }
}

var builder = WebApplication.CreateBuilder(args);

// Hosts like Render/Railway inject a PORT variable the app must listen on.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    // The API contract shapes every error as { "error": string }.
    options.InvalidModelStateResponseFactory = context =>
    {
        var firstError = context.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .SelectMany(kv => kv.Value!.Errors)
            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid request." : e.ErrorMessage)
            .FirstOrDefault() ?? "Invalid request.";
        return new BadRequestObjectResult(new { error = firstError });
    };
});
builder.Services.AddSignalR();
builder.Services.AddScoped<TokenService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// In production set FRONTEND_ORIGINS to the deployed frontend URL(s),
// comma-separated, e.g. "https://nook.vercel.app".
var corsOrigins = (builder.Configuration["FRONTEND_ORIGINS"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        };

        // SignalR sends the JWT as a query string parameter on the websocket handshake.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var creator = db.Database.GetService<IRelationalDatabaseCreator>();

    if (!creator.Exists())
    {
        // Fresh database (e.g. local dev): create database, schema and tables.
        db.Database.EnsureCreated();
    }
    else
    {
        // Existing, possibly shared database (e.g. Render): leave other
        // schemas untouched and create the "nook" schema + tables if missing.
        db.Database.ExecuteSqlRaw("CREATE SCHEMA IF NOT EXISTS nook");

        var hasAnyTables = db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'nook'")
            .AsEnumerable()
            .First() > 0;

        // Rev 2 renamed the "Floors" table/entity to "Offices" (and Workspace's
        // FloorId FK to OfficeId). That's not a compatible in-place shape change,
        // and there are no EF migrations in this project (tables are created
        // directly from the model). If the schema still has the old Rev 1 shape
        // (no "Offices" table), drop and recreate everything under "nook" —
        // this holds only pre-production seed/demo data, so resetting it is safe.
        var hasOfficesTable = db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables " +
                "WHERE table_schema = 'nook' AND table_name = 'Offices'")
            .AsEnumerable()
            .First() > 0;

        if (hasAnyTables && !hasOfficesTable)
        {
            db.Database.ExecuteSqlRaw("DROP SCHEMA nook CASCADE");
            db.Database.ExecuteSqlRaw("CREATE SCHEMA nook");
            creator.CreateTables();
        }
        else if (!hasAnyTables)
        {
            creator.CreateTables();
        }
    }

    DbSeeder.Seed(db);
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
