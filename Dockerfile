# --- Stage 1: build the front ---
# On the BUILD platform too: its output is files a browser reads, the same for every architecture.
FROM --platform=$BUILDPLATFORM node:24.22.3-slim@sha256:dc26ae92c9fb15cf6b1d63e20f0944b2efe0f115b20e00be48fb23fceb061c54 AS front
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
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
WORKDIR /src

# Everything under backend/ but the tests (.dockerignore). --locked-mode: the image is built from the
# exact package graph the committed lock files describe. The packages are kept in a cache of the
# builder, so that a change to the sources does not download them again where that cache lasts.
# No apphost: the image starts the app with `dotnet`, and the one built here is the build platform's.
COPY global.json ./
COPY backend/ ./backend/
RUN --mount=type=cache,target=/root/.nuget/packages     dotnet restore backend/Respawn.Host/Respawn.Host.csproj --locked-mode     && dotnet publish backend/Respawn.Host/Respawn.Host.csproj --configuration Release --output /publish --no-restore -p:UseAppHost=false

# --- Stage 3: runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:ca396ece3f2a2e62c7a03f1d7d710b8bc7e8c9dea5597d091178d9f5bfca13f0

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

# Where the covers are kept when RESPAWN_DATA_DIR=/data: owned by the image's user, so that a volume
# created on it (docker compose, docker run -v name:/data) can be written. Nothing else is written outside /tmp.
RUN mkdir /data && chown 10001:10001 /data

# Any non-root user works: given a data directory, the app writes there and in /tmp only.
USER 10001:10001
EXPOSE 8080

# Exec form, so the app is PID 1 and stops cleanly on SIGTERM.
ENTRYPOINT ["dotnet", "Respawn.Host.dll"]
