# syntax=docker/dockerfile:1
# MCP server in Streamable HTTP mode. Build context: repository root.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PublicData.McpServer/PublicData.McpServer.csproj src/PublicData.McpServer/
RUN dotnet restore src/PublicData.McpServer/PublicData.McpServer.csproj
COPY src/PublicData.McpServer/ src/PublicData.McpServer/
RUN dotnet publish src/PublicData.McpServer/PublicData.McpServer.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV MCP_TRANSPORT=http \
    ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=6 \
    CMD ["dotnet", "PublicData.McpServer.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "PublicData.McpServer.dll"]
