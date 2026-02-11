# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy csproj and restore
COPY src/SamlScimReference.Web/*.csproj ./src/SamlScimReference.Web/
RUN dotnet restore ./src/SamlScimReference.Web/SamlScimReference.Web.csproj

# Copy everything else and build
COPY src/SamlScimReference.Web/. ./src/SamlScimReference.Web/
WORKDIR /source/src/SamlScimReference.Web
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create data directory for SQLite
RUN mkdir -p /app/data && chmod 777 /app/data

# Copy published app
COPY --from=build /app/publish .

# Expose ports
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV Database__Path=/app/data/app.db

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SamlScimReference.Web.dll"]
