# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BloodDonorFinder.Api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Default SQLite location; mount a volume at /app/data to persist across deploys.
RUN mkdir -p /app/data
ENV ConnectionStrings__Default="Data Source=/app/data/blooddonor.db"

EXPOSE 8080
ENTRYPOINT ["dotnet", "BloodDonorFinder.Api.dll"]
