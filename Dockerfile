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
# that resolves from inside the container (a managed database's *external*
# hostname, not a provider-internal one, unless this runs in the same network):
#   ConnectionStrings__Default="Host=...;Port=5432;Database=nook;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true"
# The app exits with a clear log line if it's missing or unreachable.

EXPOSE 8080
ENTRYPOINT ["dotnet", "Nook.Api.dll"]
