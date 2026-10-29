# syntax=docker/dockerfile:1
# Build context: backend/. Targets: `api` (default, last stage), `worker`, `api-local` (Compose only) and `core-banking`.
#   docker build -t disputedesk-api backend
#   docker build -t disputedesk-worker --target worker backend
# Images are pinned by tag and by multi-arch index digest. The SDK tag matches global.json.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra@sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.14-noble-chiseled-extra@sha256:2fbc014e87df3d50948f8710f200ddca0183388904ec35c8ad7e481ef6f8a230
ARG PROBE_IMAGE=busybox:1.38.0-musl@sha256:ea2b9914a16a4ac1981994af97b318f7c7d4db76b580c56177f08bf76f4a0be8

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

# Local Compose only: the api image plus a static busybox installed under the applet name `wget`, so Compose
# can probe /health/ready (chiseled images have no shell or HTTP client). Cloud load balancers probe over
# HTTP and use `api` unchanged.
FROM ${PROBE_IMAGE} AS probe
FROM api AS api-local
COPY --from=probe /bin/busybox /usr/local/bin/wget

# The core-banking stub (AD-21): its own restore and publish, since it references no DisputeDesk project. It reads
# fixtures/local.json, which Compose mounts at runtime. It only exists for local and demo stacks, so the image
# carries the same wget probe as api-local for its Compose healthcheck.
FROM ${SDK_IMAGE} AS publish-core-banking
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY stubs/DisputeDesk.CoreBankingStub/DisputeDesk.CoreBankingStub.csproj stubs/DisputeDesk.CoreBankingStub/
RUN dotnet restore stubs/DisputeDesk.CoreBankingStub/DisputeDesk.CoreBankingStub.csproj
COPY stubs/DisputeDesk.CoreBankingStub/ stubs/DisputeDesk.CoreBankingStub/
RUN dotnet publish stubs/DisputeDesk.CoreBankingStub/DisputeDesk.CoreBankingStub.csproj -c Release --no-restore -o /app

FROM ${ASPNET_IMAGE} AS core-banking
WORKDIR /app
COPY --from=publish-core-banking /app .
COPY --from=probe /bin/busybox /usr/local/bin/wget
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DisputeDesk.CoreBankingStub.dll"]

# Keep `api` the default target: the last stage is the production image.
FROM api
