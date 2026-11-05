# Build from the repo root: docker build -f backend/Dockerfile -t zai-backend .

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore packages first, so packages are cached between code changes
COPY backend/backend.csproj backend/
RUN dotnet restore backend/backend.csproj

COPY backend/ backend/
COPY db/ db/
RUN dotnet publish backend/backend.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Run as a non-root user
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "backend.dll"]
