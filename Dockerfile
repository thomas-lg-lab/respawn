FROM python:3.14-slim@sha256:9aaa474097d094040649f2162a627c0049b408f52138d980916788d80f04802f

LABEL org.opencontainers.image.title="respawn"
LABEL org.opencontainers.image.description="Self-hosted game library: stats, backlog and next-game suggestions"
LABEL org.opencontainers.image.url="https://github.com/thomas-lg-lab/respawn"
LABEL org.opencontainers.image.source="https://github.com/thomas-lg-lab/respawn"
LABEL org.opencontainers.image.licenses="AGPL-3.0-or-later"

# The root filesystem is read-only at run time: no bytecode files, logs straight to stdout.
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PYTHONPATH=/app/src

# Version injected at build time (--build-arg VERSION=1.2.3)
ARG VERSION=dev
ENV APP_VERSION=$VERSION

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir --disable-pip-version-check -r requirements.txt

COPY src/ src/

# Any non-root user works: the app writes nowhere but /tmp.
USER 10001:10001
EXPOSE 8080

# Exec form, so uvicorn is PID 1 and stops cleanly on SIGTERM. FORWARDED_ALLOW_IPS names the reverse proxies whose
# X-Forwarded-For and X-Forwarded-Proto are trusted (uvicorn reads it; default 127.0.0.1). Behind a proxy it must
# be set, or the sign-in rate limit counts every visitor as the proxy (README, "Behind a reverse proxy").
CMD ["python", "-m", "uvicorn", "respawn.main:app", "--host", "0.0.0.0", "--port", "8080", "--proxy-headers"]
