# =====================================================================
#  Sangeet API — container image for Render (or any Docker host).
#
#  Build from the REPOSITORY ROOT, because the API references sibling projects:
#      docker build -t sangeet-api .
#
#  Run locally:
#      docker run --rm -p 5000:5000 -e PORT=5000 \
#        -e ConnectionStrings__MusicDatabase="postgresql://..." \
#        -e Jwt__Key="<32+ char secret>" -e Jwt__Issuer=MusicWebsite -e Jwt__Audience=MusicWebsiteClient \
#        sangeet-api
#
#  NOTE: no secrets are baked in. appsettings.json is gitignored and excluded by .dockerignore,
#  so every setting arrives as an environment variable. StartupConfigCheck lists any that are
#  missing instead of failing with a cryptic error.
# =====================================================================

# ---------------------------------------------------------------- build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the project files first so "dotnet restore" is cached and does not re-run on every
# source edit. (tools/DbMigrator is a local-only migration utility and is deliberately not built.)
COPY MusicWebsite/MusicWebsite/MusicWebsite.csproj                           MusicWebsite/MusicWebsite/
COPY MusicWebsite/MusicWebsite.Application/MusicWebsite.Application.csproj   MusicWebsite/MusicWebsite.Application/
COPY MusicWebsite/MusicWebsite.Domain/MusicWebsite.Domain.csproj             MusicWebsite/MusicWebsite.Domain/
COPY MusicWebsite/MusicWebsite.Infrastructure/MusicWebsite.Infrastructure.csproj MusicWebsite/MusicWebsite.Infrastructure/

RUN dotnet restore MusicWebsite/MusicWebsite/MusicWebsite.csproj

# Now the sources, and publish.
COPY MusicWebsite/ MusicWebsite/
RUN dotnet publish MusicWebsite/MusicWebsite/MusicWebsite.csproj \
        -c Release -o /app/publish \
        --no-restore \
        -p:UseAppHost=false

# -------------------------------------------------------------- runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# yt-dlp version.
#
# THIS GOES STALE AND THE FEATURE BREAKS. YouTube changes its extraction defences
# constantly and yt-dlp ships fixes most weeks; a build more than a few months old
# starts failing on every video, typically with "Sign in to confirm you're not a bot".
#
# It stays pinned rather than tracking "latest" because Docker layer caching would
# otherwise keep serving whatever binary was downloaded the first time — a pin that
# you bump is honest, an unpinned URL that never re-downloads is a silent lie.
# Bumping this value changes the layer and forces a fresh download.
#
# Latest releases: https://github.com/yt-dlp/yt-dlp/releases
ARG YTDLP_VERSION=2026.07.04

# ca-certificates: TLS to Supabase, Backblaze B2 and YouTube.
# yt-dlp: the standalone Linux build is a self-contained PyInstaller binary — no Python needed.
#         ffmpeg is NOT installed: the extractor downloads "bestaudio[ext=m4a]" and never
#         transcodes, which keeps the image small and avoids heavy CPU work on small instances.
#
# curl is kept rather than purged: "apt-get purge --auto-remove curl" can also drag out shared
# libraries the .NET runtime relies on, turning a working image into one that fails at run time.
# A couple of MB is a fair price, and curl is handy for debugging inside the container.
#
# "yt-dlp --version" runs at BUILD time on purpose — a bad download or wrong architecture fails
# the build here, instead of surfacing as a broken feature after deploy.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL --retry 3 -o /usr/local/bin/yt-dlp \
         "https://github.com/yt-dlp/yt-dlp/releases/download/${YTDLP_VERSION}/yt-dlp_linux" \
    && chmod 755 /usr/local/bin/yt-dlp \
    && /usr/local/bin/yt-dlp --version

COPY --from=build /app/publish .

# Defaults that make the container behave. Every one can be overridden at run time.
#   BACKEND_HOST  0.0.0.0 — binding to localhost inside a container makes it unreachable.
#   PORT          the platform (Render/Railway/Heroku) overrides this; the app prefers PORT.
#   Youtube__*    plain "yt-dlp" resolves via PATH to the Linux binary installed above.
ENV ASPNETCORE_ENVIRONMENT=Production \
    BACKEND_HOST=0.0.0.0 \
    PORT=5000 \
    SERVE_FRONTEND=false \
    Youtube__Provider=YtDlp \
    Youtube__YtDlpPath=yt-dlp \
    Youtube__TimeoutSeconds=90 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Crash guards for small/locked-down container hosts.
#   EnableWriteXorExecute=0 — .NET 7/8 enable W^X memory protection by default, a documented
#     cause of SIGSEGV (exit code 139) on some container/kernel combinations.
#   gcServer=0 — workstation GC, so the runtime doesn't size heaps for a big multi-core host
#     when it only has 512 MB and 0.1 CPU.
# (Kept as separate ENV lines: Docker does not reliably allow comments inside a continued
#  instruction, which would break the build.)
ENV DOTNET_EnableWriteXorExecute=0
ENV DOTNET_gcServer=0

# Run as the unprivileged "app" user that the .NET 8 images provide (UID 1654).
# yt-dlp writes downloads to /tmp, which is world-writable, so this needs no extra setup.
USER app

EXPOSE 5000

ENTRYPOINT ["dotnet", "MusicWebsite.dll"]
