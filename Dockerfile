# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Nook.Api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# REQUIRED at runtime — backend/.env is excluded by .dockerignore, so the
# connection string must come from the container's environment. Use a hostname
# that resolves from inside the container: a managed database's *external*
# hostname, not a provider-internal one (Render's "dpg-...-a" resolves only
# within its own region, and not at all from outside Render). Either form works:
#   ConnectionStrings__Default="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
#   DATABASE_URL="postgresql://user:pass@host.oregon-postgres.render.com/dbname"
# ConnectionStrings__Default is read first and shadows DATABASE_URL if both are
# set. The app logs the host it resolved, then exits with a clear line if that
# host is missing or unreachable rather than crash-looping on a stack trace.

EXPOSE 8080
ENTRYPOINT ["dotnet", "Nook.Api.dll"]
