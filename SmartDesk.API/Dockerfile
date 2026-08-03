# WHY MULTI-STAGE BUILD?
# Stage 1 (build): uses the full SDK image to compile the app
# Stage 2 (runtime): uses the smaller runtime-only image
# Result: production image is ~200MB instead of ~700MB
# Smaller image = faster deployment, less attack surface

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first (Docker layer caching)
# WHY copy .csproj files before source? If source changes but dependencies don't,
# Docker reuses the cached restore layer — much faster builds
COPY SmartDesk.Domain/SmartDesk.Domain.csproj SmartDesk.Domain/
COPY SmartDesk.Application/SmartDesk.Application.csproj SmartDesk.Application/
COPY SmartDesk.Infrastructure/SmartDesk.Infrastructure.csproj SmartDesk.Infrastructure/
COPY SmartDesk.API/SmartDesk.API.csproj SmartDesk.API/

RUN dotnet restore SmartDesk.API/SmartDesk.API.csproj

# Copy all source and build
COPY SmartDesk.Domain/ SmartDesk.Domain/
COPY SmartDesk.Application/ SmartDesk.Application/
COPY SmartDesk.Infrastructure/ SmartDesk.Infrastructure/
COPY SmartDesk.API/ SmartDesk.API/

RUN dotnet publish SmartDesk.API/SmartDesk.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime only
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user for security — never run containers as root
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .

# Azure App Service expects port 8080
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "SmartDesk.API.dll"]
