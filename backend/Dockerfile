# syntax=docker/dockerfile:1.7
# Multi-stage build for the SACCO API (and, via the seed target, the migration/seed tool).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config ./
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/Sacco.sln backend/
COPY backend/src backend/src
COPY backend/seed backend/seed
COPY backend/tests backend/tests
COPY shared-contracts/openapi shared-contracts/openapi
RUN dotnet restore backend/Sacco.sln
RUN dotnet publish backend/src/Sacco.Api/Sacco.Api.csproj -c Release -o /out/api --no-restore -p:OpenApiGenerateDocumentsOnBuild=false \
 && dotnet publish backend/seed/Sacco.Seed/Sacco.Seed.csproj -c Release -o /out/seed --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
# System Chromium for the nightly PDF digest (ADR 0013, Sacco.Modules.Reporting): PuppeteerSharp is pointed at
# it via Reporting__DailyDigest__ChromiumExecutablePath so the container never downloads a browser at runtime.
RUN apt-get update && apt-get install -y --no-install-recommends chromium fonts-liberation fonts-noto-color-emoji \
    && rm -rf /var/lib/apt/lists/*
RUN groupadd -r sacco && useradd -r -g sacco sacco
COPY --from=build /out/api .
USER sacco
ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0 Reporting__DailyDigest__ChromiumExecutablePath=/usr/bin/chromium
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s CMD ["/bin/sh", "-c", "wget -qO- http://localhost:8080/health || exit 1"]
ENTRYPOINT ["dotnet", "Sacco.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS seed
WORKDIR /app
RUN groupadd -r sacco && useradd -r -g sacco sacco
COPY --from=build /out/seed .
USER sacco
ENTRYPOINT ["dotnet", "Sacco.Seed.dll"]
