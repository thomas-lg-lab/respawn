# --- Stage 1: build the front ---
# On the BUILD platform too: its output is files a browser reads, the same for every architecture.
FROM --platform=$BUILDPLATFORM node:24-slim@sha256:dc26ae92c9fb15cf6b1d63e20f0944b2efe0f115b20e00be48fb23fceb061c54 AS front
WORKDIR /web

# The dependencies first, in a layer of their own: npm ci installs exactly what the committed lock
# file describes, and runs again only when it changes.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# --- Stage 2: publish the host ---
# Pinned to the BUILD platform, not the target: the publish is portable (no runtime identifier), so
# its output is architecture-neutral IL plus a runtimes/ folder carrying every native asset. The
# runtime stage below is what makes the image amd64 or arm64.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src

# Everything under backend/ but the tests (.dockerignore). --locked-mode: the image is built from the
# exact package graph the committed lock files describe.
COPY global.json ./
COPY backend/ ./backend/
RUN dotnet restore backend/Respawn.Host/Respawn.Host.csproj --locked-mode
RUN dotnet publish backend/Respawn.Host/Respawn.Host.csproj --configuration Release --output /publish --no-restore

# --- Stage 3: runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e

LABEL org.opencontainers.image.title="respawn"
LABEL org.opencontainers.image.description="Self-hosted game library: stats, backlog and next-game suggestions"
LABEL org.opencontainers.image.url="https://github.com/thomas-lg-lab/respawn"
LABEL org.opencontainers.image.source="https://github.com/thomas-lg-lab/respawn"
LABEL org.opencontainers.image.licenses="AGPL-3.0-or-later"

# Version injected at build time (--build-arg VERSION=1.2.3)
ARG VERSION=dev

# The root filesystem is read-only at run time: logs go straight to stdout, and the runtime's
# diagnostics, which would create a socket under /tmp, are off.
ENV APP_VERSION=$VERSION \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

WORKDIR /app
COPY --from=build /publish ./
# The front, where the host serves its files from.
COPY --from=front /web/dist/app/browser ./wwwroot/

# Any non-root user works: the app writes nowhere.
USER 10001:10001
EXPOSE 8080

# Exec form, so the app is PID 1 and stops cleanly on SIGTERM.
ENTRYPOINT ["dotnet", "Respawn.Host.dll"]
