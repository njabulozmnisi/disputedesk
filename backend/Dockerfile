# syntax=docker/dockerfile:1
# Build context: backend/. Targets: `api` (default, last stage) and `worker`.
#   docker build -t disputedesk-api backend
#   docker build -t disputedesk-worker --target worker backend
# Images are pinned by tag and by multi-arch index digest. The SDK tag matches global.json.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra@sha256:9e3919aff68057f3e45125c5255a71d3b5f40364e45ede883348688851b9f0df
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.13-noble-chiseled-extra@sha256:2e4f4e0a8f0807397c91585b3b6142989b43e9030a6494af1b7abd251b915de1

FROM ${SDK_IMAGE} AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/DisputeDesk.Domain/DisputeDesk.Domain.csproj src/DisputeDesk.Domain/
COPY src/DisputeDesk.Application/DisputeDesk.Application.csproj src/DisputeDesk.Application/
COPY src/DisputeDesk.Infrastructure/DisputeDesk.Infrastructure.csproj src/DisputeDesk.Infrastructure/
COPY src/DisputeDesk.Contracts/DisputeDesk.Contracts.csproj src/DisputeDesk.Contracts/
COPY src/DisputeDesk.Api/DisputeDesk.Api.csproj src/DisputeDesk.Api/
COPY src/DisputeDesk.Worker/DisputeDesk.Worker.csproj src/DisputeDesk.Worker/
RUN dotnet restore src/DisputeDesk.Api/DisputeDesk.Api.csproj \
 && dotnet restore src/DisputeDesk.Worker/DisputeDesk.Worker.csproj
COPY src/ src/

FROM build AS publish-api
# openapi.json is generated and checked in from developer builds; the image never writes it.
RUN dotnet publish src/DisputeDesk.Api/DisputeDesk.Api.csproj -c Release --no-restore -o /app \
    -p:OpenApiGenerateDocuments=false

FROM build AS publish-worker
RUN dotnet publish src/DisputeDesk.Worker/DisputeDesk.Worker.csproj -c Release --no-restore -o /app

FROM ${RUNTIME_IMAGE} AS worker
WORKDIR /app
COPY --from=publish-worker /app .
# Chiseled images ship a non-root `app` user (APP_UID) and no shell or package manager.
USER $APP_UID
ENTRYPOINT ["dotnet", "DisputeDesk.Worker.dll"]

FROM ${ASPNET_IMAGE} AS api
WORKDIR /app
COPY --from=publish-api /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DisputeDesk.Api.dll"]
