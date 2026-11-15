# syntax=docker/dockerfile:1
# Single container: ASP.NET Core API serving the production Angular build from the same origin.

# ---- 1. Angular build -------------------------------------------------------
FROM node:26.13.1-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- 2. API publish ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS api
WORKDIR /src
COPY api/SiteRoof.Api/SiteRoof.Api.csproj api/SiteRoof.Api/
RUN dotnet restore api/SiteRoof.Api/SiteRoof.Api.csproj
COPY api/ api/
RUN dotnet publish api/SiteRoof.Api/SiteRoof.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

# ---- 3. Runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0
COPY --from=api /out ./
COPY --from=web /src/web/dist/web/browser ./wwwroot
# Non-root user provided by the .NET base image.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SiteRoof.Api.dll"]
