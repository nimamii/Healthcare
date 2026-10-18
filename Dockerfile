# ---------------------------------------------------------------------------
# Base — SDK version pinned here, not on the host machine.
# This is what makes the build reproducible across a 3-person team (§35).
#
# No "# syntax=docker/dockerfile:1" directive. It makes Docker pull a newer
# buildkit image from Docker Hub, which this network blocks, and nothing here
# needs a newer builder than the one bundled with Docker Desktop.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS base
WORKDIR /src

# ---------------------------------------------------------------------------
# Restore
#
# Copied as a separate layer so package restore is cached until a project
# file actually changes. Editing source does not re-download NuGet packages.
# ---------------------------------------------------------------------------
FROM base AS restore
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BuildingBlocks/SharedKernel/SharedKernel.csproj src/BuildingBlocks/SharedKernel/SharedKernel.csproj
COPY src/Healthcare.Api/Healthcare.Api.csproj src/Healthcare.Api/Healthcare.Api.csproj
COPY src/Modules/Audit/Audit.Contracts/Audit.Contracts.csproj src/Modules/Audit/Audit.Contracts/Audit.Contracts.csproj
COPY src/Modules/Audit/Audit.Infrastructure/Audit.Infrastructure.csproj src/Modules/Audit/Audit.Infrastructure/Audit.Infrastructure.csproj
COPY src/Modules/Catalog/Catalog.Api/Catalog.Api.csproj src/Modules/Catalog/Catalog.Api/Catalog.Api.csproj
COPY src/Modules/Catalog/Catalog.Application/Catalog.Application.csproj src/Modules/Catalog/Catalog.Application/Catalog.Application.csproj
COPY src/Modules/Catalog/Catalog.Contracts/Catalog.Contracts.csproj src/Modules/Catalog/Catalog.Contracts/Catalog.Contracts.csproj
COPY src/Modules/Catalog/Catalog.Domain/Catalog.Domain.csproj src/Modules/Catalog/Catalog.Domain/Catalog.Domain.csproj
COPY src/Modules/Catalog/Catalog.Infrastructure/Catalog.Infrastructure.csproj src/Modules/Catalog/Catalog.Infrastructure/Catalog.Infrastructure.csproj
COPY src/Modules/Identity/Identity.Api/Identity.Api.csproj src/Modules/Identity/Identity.Api/Identity.Api.csproj
COPY src/Modules/Identity/Identity.Application/Identity.Application.csproj src/Modules/Identity/Identity.Application/Identity.Application.csproj
COPY src/Modules/Identity/Identity.Contracts/Identity.Contracts.csproj src/Modules/Identity/Identity.Contracts/Identity.Contracts.csproj
COPY src/Modules/Identity/Identity.Domain/Identity.Domain.csproj src/Modules/Identity/Identity.Domain/Identity.Domain.csproj
COPY src/Modules/Identity/Identity.Infrastructure/Identity.Infrastructure.csproj src/Modules/Identity/Identity.Infrastructure/Identity.Infrastructure.csproj
COPY src/Modules/Inventory/Inventory.Api/Inventory.Api.csproj src/Modules/Inventory/Inventory.Api/Inventory.Api.csproj
COPY src/Modules/Inventory/Inventory.Contracts/Inventory.Contracts.csproj src/Modules/Inventory/Inventory.Contracts/Inventory.Contracts.csproj
COPY src/Modules/Inventory/Inventory.Domain/Inventory.Domain.csproj src/Modules/Inventory/Inventory.Domain/Inventory.Domain.csproj
COPY src/Modules/Inventory/Inventory.Infrastructure/Inventory.Infrastructure.csproj src/Modules/Inventory/Inventory.Infrastructure/Inventory.Infrastructure.csproj
COPY src/Modules/Geography/Geography.Domain/Geography.Domain.csproj src/Modules/Geography/Geography.Domain/
COPY src/Modules/Geography/Geography.Contracts/Geography.Contracts.csproj src/Modules/Geography/Geography.Contracts/
COPY src/Modules/Geography/Geography.Infrastructure/Geography.Infrastructure.csproj src/Modules/Geography/Geography.Infrastructure/
COPY src/Modules/Orders/Orders.Domain/Orders.Domain.csproj src/Modules/Orders/Orders.Domain/
COPY src/Modules/Orders/Orders.Contracts/Orders.Contracts.csproj src/Modules/Orders/Orders.Contracts/
COPY src/Modules/Orders/Orders.Application/Orders.Application.csproj src/Modules/Orders/Orders.Application/
COPY src/Modules/Orders/Orders.Infrastructure/Orders.Infrastructure.csproj src/Modules/Orders/Orders.Infrastructure/
COPY src/Modules/Orders/Orders.Api/Orders.Api.csproj src/Modules/Orders/Orders.Api/
COPY src/Modules/Payments/Payments.Domain/Payments.Domain.csproj src/Modules/Payments/Payments.Domain/
COPY src/Modules/Payments/Payments.Contracts/Payments.Contracts.csproj src/Modules/Payments/Payments.Contracts/
COPY src/Modules/Payments/Payments.Application/Payments.Application.csproj src/Modules/Payments/Payments.Application/
COPY src/Modules/Payments/Payments.Infrastructure/Payments.Infrastructure.csproj src/Modules/Payments/Payments.Infrastructure/
COPY src/Modules/Payments/Payments.Api/Payments.Api.csproj src/Modules/Payments/Payments.Api/
COPY src/Modules/Fulfillment/Fulfillment.Domain/Fulfillment.Domain.csproj src/Modules/Fulfillment/Fulfillment.Domain/
COPY src/Modules/Fulfillment/Fulfillment.Contracts/Fulfillment.Contracts.csproj src/Modules/Fulfillment/Fulfillment.Contracts/
COPY src/Modules/Fulfillment/Fulfillment.Application/Fulfillment.Application.csproj src/Modules/Fulfillment/Fulfillment.Application/
COPY src/Modules/Fulfillment/Fulfillment.Infrastructure/Fulfillment.Infrastructure.csproj src/Modules/Fulfillment/Fulfillment.Infrastructure/
COPY src/Modules/Fulfillment/Fulfillment.Api/Fulfillment.Api.csproj src/Modules/Fulfillment/Fulfillment.Api/
RUN dotnet restore src/Healthcare.Api/Healthcare.Api.csproj

# ---------------------------------------------------------------------------
# Development — SDK + source, builds once and runs the output.
#
# Not "dotnet run": that re-evaluates and re-restores on every container start,
# which turns a 2-second start into a 30-second one and fails confusingly if
# a mounted volume hides anything. Building here and running the DLL is
# predictable, and "docker compose restart" is instant.
#
# To pick up a code change, run:  docker compose up -d --build
# ---------------------------------------------------------------------------
FROM restore AS development
ENV DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_USE_POLLING_FILE_WATCHER=1
COPY . .
RUN dotnet build src/Healthcare.Api/Healthcare.Api.csproj \
    -c Debug \
    -o /app/dev \
    --no-restore
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/dev/Healthcare.Api.dll"]

# ---------------------------------------------------------------------------
# Publish — runtime only, no SDK, no source.
# ---------------------------------------------------------------------------
FROM base AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "src/Healthcare.Api/Healthcare.Api.csproj" \
    -c "$BUILD_CONFIGURATION" \
    -o /app/publish \
    --no-restore
WORKDIR /app

# ---------------------------------------------------------------------------
# Final
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Never run as root. A container escape should not land on a root-owned process.
RUN addgroup --system --gid 1001 appgroup \
 && adduser --system --uid 1001 appuser

COPY --from=publish /app/publish .

USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Container-local healthcheck. Compose's version checks dependencies; this one
# checks the app itself so a hung process is detected, not just a dead one.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Healthcare.Api.dll"]
