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

# Provide the Postgres connection string at runtime, e.g.
# ConnectionStrings__Default="Host=...;Port=5432;Database=nook;Username=...;Password=..."

EXPOSE 8080
ENTRYPOINT ["dotnet", "Nook.Api.dll"]
