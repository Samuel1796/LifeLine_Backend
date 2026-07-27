using System.Net.Sockets;
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
using Npgsql;

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

// In production the database must come from an explicit environment value.
// Local development can still fall back to appsettings.json when .env is absent.
var connectionString = ResolveConnectionString(builder.Configuration, builder.Environment);
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Startup failed: no PostgreSQL connection string. Set ConnectionStrings__Default or " +
        "DATABASE_URL to an externally reachable PostgreSQL URL.");
    return 1;
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

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

// A managed database is often still coming up (or its DNS still propagating) when
// the container starts, so retry before giving up. Letting the first failure escape
// as an unhandled exception just crash-loops the container, and the raw Npgsql stack
// trace never says which host it tried — so log that up front.
var dbTarget = new NpgsqlConnectionStringBuilder(connectionString);
const int maxDbAttempts = 5;

app.Logger.LogInformation(
    "Using PostgreSQL host {Host}:{Port}, database {Database}, user {Username}",
    dbTarget.Host, dbTarget.Port, dbTarget.Database, dbTarget.Username);

for (var attempt = 1; ; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        InitializeDatabase(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        break;
    }
    catch (Exception ex) when (IsConnectivityFailure(ex))
    {
        if (attempt == maxDbAttempts)
        {
            app.Logger.LogError(
                "Could not reach PostgreSQL at {Host}:{Port} after {Attempts} attempts: {Message}{Hint}",
                dbTarget.Host, dbTarget.Port, maxDbAttempts, InnermostMessage(ex), HintFor(ex, dbTarget.Host));
            return 1;
        }

        var delay = TimeSpan.FromSeconds(attempt * 2);
        app.Logger.LogWarning(
            "PostgreSQL at {Host}:{Port} not reachable (attempt {Attempt}/{Attempts}): {Message}. Retrying in {Delay}s.",
            dbTarget.Host, dbTarget.Port, attempt, maxDbAttempts, InnermostMessage(ex), delay.TotalSeconds);
        Thread.Sleep(delay);
    }
    catch (Exception ex)
    {
        // Reached the server but couldn't initialize. Either way this won't fix
        // itself on a restart, so report it in one line and exit rather than
        // dumping a stack trace on every container restart.
        if (AsPostgresException(ex) is { } postgres)
            app.Logger.LogError(
                "PostgreSQL at {Host}:{Port} rejected database {Database} as user {Username} ({SqlState}): {Message}",
                dbTarget.Host, dbTarget.Port, dbTarget.Database, dbTarget.Username,
                postgres.SqlState, postgres.MessageText);
        else
            app.Logger.LogError(ex, "Initializing the database at {Host}:{Port} failed.",
                dbTarget.Host, dbTarget.Port);

        return 1;
    }
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
return 0;

// Creates the schema and tables when they're missing, then seeds demo data.
static void InitializeDatabase(AppDbContext db)
{
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

static string? ResolveConnectionString(IConfiguration configuration, IHostEnvironment environment)
{
    if (TryResolveConnectionString(Environment.GetEnvironmentVariable("ConnectionStrings__Default"), out var connectionString))
        return connectionString;

    if (TryResolveDatabaseUrl(Environment.GetEnvironmentVariable("DATABASE_URL"), out connectionString))
        return connectionString;

    if (environment.IsDevelopment() && TryResolveConnectionString(configuration.GetConnectionString("Default"), out connectionString))
        return connectionString;

    return null;
}

static bool TryResolveConnectionString(string? rawValue, out string connectionString)
{
    connectionString = string.Empty;

    if (string.IsNullOrWhiteSpace(rawValue))
        return false;

    if (rawValue.Contains("://", StringComparison.Ordinal))
        return TryResolveDatabaseUrl(rawValue, out connectionString);

    connectionString = rawValue;
    return true;
}

static bool TryResolveDatabaseUrl(string? rawValue, out string connectionString)
{
    connectionString = string.Empty;

    if (string.IsNullOrWhiteSpace(rawValue))
        return false;

    if (!Uri.TryCreate(rawValue, UriKind.Absolute, out var uri))
        return false;

    if (uri.Scheme is not ("postgres" or "postgresql"))
        return false;

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = uri.AbsolutePath.Trim('/'),
    };

    if (!string.IsNullOrWhiteSpace(uri.UserInfo))
    {
        var userInfo = uri.UserInfo.Split(':', 2);
        builder.Username = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1)
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
    }

    foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var separator = pair.IndexOf('=');
        var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]).Trim();
        var value = Uri.UnescapeDataString(separator < 0 ? string.Empty : pair[(separator + 1)..]).Trim();

        if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode))
            builder.SslMode = sslMode;
        else if (key.Equals("trustservercertificate", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var trustServerCertificate))
            builder.TrustServerCertificate = trustServerCertificate;
        else if (key.Equals("timeout", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var timeout))
            builder.Timeout = timeout;
        else if (key.Equals("commandtimeout", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var commandTimeout))
            builder.CommandTimeout = commandTimeout;
    }

    connectionString = builder.ConnectionString;
    return true;
}

// True when the server was never reached (DNS, TCP, TLS, timeout). A
// PostgresException means it answered — that's a config problem, not a blip,
// so it isn't worth retrying.
static bool IsConnectivityFailure(Exception exception)
{
    for (var e = exception; e is not null; e = e.InnerException)
    {
        if (e is PostgresException) return false;
        if (e is NpgsqlException or SocketException or TimeoutException) return true;
    }

    return false;
}

// EF sometimes wraps the server's error response, so search the whole chain.
static PostgresException? AsPostgresException(Exception exception)
{
    for (var e = exception; e is not null; e = e.InnerException)
        if (e is PostgresException postgres) return postgres;

    return null;
}

// EF wraps the real cause two or three layers deep; only the innermost message
// ("Name or service not known") says anything useful.
static string InnermostMessage(Exception exception)
{
    var innermost = exception;
    while (innermost.InnerException is not null) innermost = innermost.InnerException;
    return innermost.Message.TrimEnd('.', ' ');
}

static string HintFor(Exception exception, string? host)
{
    for (var e = exception; e is not null; e = e.InnerException)
    {
        if (e is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData })
            return $" Hostname '{host}' could not be resolved. Use the external PostgreSQL host " +
                   "or set DATABASE_URL / ConnectionStrings__Default to the full externally reachable " +
                   "connection string — backend/.env is excluded from the Docker image, so its value " +
                   "never reaches a deployed container.";
    }

    return string.Empty;
}
