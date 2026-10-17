# SivayaanHMS API — Linux container, built from the repository root:
#
#     docker build -f backend/Dockerfile -t sivayaanhms-api:latest .
#
# Linux and not Windows, deliberately, even when the host is a Windows box.
# A .NET image is per-OS — there is no one image that runs as both a Linux
# and a Windows container — and this application needs nothing from Windows
# (no System.Drawing, no registry, no Windows paths; audited). A Linux image
# is ~250 MB against ~2-5 GB, and Docker Desktop runs it on Windows through
# WSL2, so one image serves both hosts. See docs/DEPLOY_DOCKER.md.

# ── build ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so `restore` is cached and only re-runs when a
# dependency actually changes rather than on every source edit.
COPY backend/src/SivayaanHMS.Core/SivayaanHMS.Core.csproj       backend/src/SivayaanHMS.Core/
COPY backend/src/SivayaanHMS.Data/SivayaanHMS.Data.csproj       backend/src/SivayaanHMS.Data/
COPY backend/src/SivayaanHMS.Printing/SivayaanHMS.Printing.csproj backend/src/SivayaanHMS.Printing/
COPY backend/src/SivayaanHMS.Api/SivayaanHMS.Api.csproj         backend/src/SivayaanHMS.Api/
RUN dotnet restore backend/src/SivayaanHMS.Api/SivayaanHMS.Api.csproj

COPY backend/src backend/src

# No -r: a portable publish, so the Linux-native SkiaSharp and qpdf assets
# come from NuGet for this image's architecture rather than the win-x64 ones
# the Windows deployment carries.
RUN dotnet publish backend/src/SivayaanHMS.Api/SivayaanHMS.Api.csproj \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

# The developer's secrets file must never reach an image. It is excluded by
# the csproj's CopyToPublishDirectory="Never" and by .dockerignore; this
# fails the build loudly if either guard ever regresses.
RUN test ! -f /app/publish/appsettings.Local.json \
      || (echo "appsettings.Local.json reached the publish output" && exit 1)

# ── runtime ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# libfontconfig1 — QuestPDF renders through Skia, which resolves fonts via
#   fontconfig on Linux. Without it the first PDF throws rather than the
#   hundredth, but it throws.
# fonts-dejavu-core — an actual font for it to find. The documents ask for
#   "Segoe UI" (DocumentStyle.BodyFont), which exists only on Windows; the
#   fontconfig alias copied in below points that name at DejaVu Sans so a
#   receipt prints rather than failing on a missing family. For output
#   identical to the Windows box, set Document branding → print font to
#   "Lato", which QuestPDF bundles and registers in-process on every OS.
# tzdata — not in the base image, and this application stores clinic-local
#   wall-clock time (SystemClock → DateTime.Now). Without it TZ below cannot
#   resolve and every timestamp is UTC.
# curl — for HEALTHCHECK only.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core tzdata curl \
 && rm -rf /var/lib/apt/lists/*

COPY deploy/docker/fonts-local.conf /etc/fonts/local.conf

# The single most consequential line in this file. Containers default to UTC;
# this schema stores wall-clock times with no offset, so on UTC every visit
# time, receipt time and day-book boundary silently shifts by 5½ hours and
# nothing errors. Override it per deployment (compose does) if the clinic is
# not in India.
ENV TZ=Asia/Kolkata

# 0.0.0.0, not localhost: inside a container "localhost" is the container's
# own loopback, so a published port would reach nothing. Set as the Kestrel
# configuration key rather than ASPNETCORE_URLS, because an appsettings
# Kestrel section overrides the latter — which is exactly how an hour went
# missing once (docs/DEPLOY_IIS.md §6).
ENV Kestrel__Endpoints__Http__Url=http://0.0.0.0:8080
EXPOSE 8080

WORKDIR /app
COPY --from=build /app/publish .

# Non-root. APP_UID is defined by the base image.
USER $APP_UID

# Liveness only. Readiness (/health/ready) also opens a database connection,
# which is the right thing for a deployment gate and the wrong thing for a
# probe that would restart the container every time the database blips.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "SivayaanHMS.Api.dll"]
