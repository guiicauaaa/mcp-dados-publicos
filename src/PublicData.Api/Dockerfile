# syntax=docker/dockerfile:1
# API + Angular frontend in one image (same origin, no CORS). Build context: repository root.

# 1) Angular: the build output goes to src/PublicData.Api/wwwroot (see angular.json).
FROM node:24-alpine AS web
WORKDIR /src/src/PublicData.Web
COPY src/PublicData.Web/package.json src/PublicData.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/PublicData.Web/ ./
RUN npm run build

# 2) .NET: restore with only the project files first, so the layer is cached while the code changes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY build/ build/
COPY ollama/Modelfile ollama/
COPY src/PublicData.McpServer/PublicData.McpServer.csproj src/PublicData.McpServer/
COPY src/PublicData.Chat.Core/PublicData.Chat.Core.csproj src/PublicData.Chat.Core/
COPY src/PublicData.Api/PublicData.Api.csproj src/PublicData.Api/
RUN dotnet restore src/PublicData.Api/PublicData.Api.csproj
COPY src/Shared/ src/Shared/
COPY src/PublicData.McpServer/ src/PublicData.McpServer/
COPY src/PublicData.Chat.Core/ src/PublicData.Chat.Core/
COPY src/PublicData.Api/ src/PublicData.Api/
RUN dotnet publish src/PublicData.Api/PublicData.Api.csproj -c Release -o /app --no-restore

# 3) Runtime.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Chat__Mcp__Transport=http
COPY --from=build /app .
COPY --from=web /src/src/PublicData.Api/wwwroot ./wwwroot
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 \
    CMD ["dotnet", "PublicData.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "PublicData.Api.dll"]
