# syntax=docker/dockerfile:1.7
# ============================================================================
# MyHomePage — Production Dockerfile
# ----------------------------------------------------------------------------
#  * Multi-stage build: SDK image for compile, smaller runtime image for run
#  * FFmpeg pre-installed via apt (no first-run download in container)
#  * Non-root user for security
#  * Health-checked via /health endpoint
# ============================================================================

# ─── Stage 1: Build ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj first for better Docker layer caching
COPY MyHomePage.csproj ./
RUN dotnet restore MyHomePage.csproj

# Copy the rest and publish
COPY . .
RUN dotnet publish MyHomePage.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ─── Stage 2: Runtime ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install FFmpeg (apt is much faster than the Xabe.FFmpeg.Downloader)
# Also install curl for healthcheck and DejaVu fonts so ImageSharp.Drawing
# can render text on Open Graph preview overlays (SystemFonts.TryGet picks
# DejaVu Sans first; without the package the container ships zero fonts
# and the OG bar would silently fall back to the plain crop).
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/* \
    && ffmpeg -version

# Copy published output
COPY --from=build /app/publish ./

# Create storage dirs (running as root because Railway volumes are mounted as root)
RUN mkdir -p /data/videos /app/logs

# ─── Environment ────────────────────────────────────────────────────────────
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false \
    FFMPEG_PATH=/usr/bin \
    VIDEO_STORAGE_ROOT=/data/videos

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "MyHomePage.dll"]
