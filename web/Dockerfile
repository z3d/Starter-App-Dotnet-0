# The web app as a static site behind nginx, for environments that host it as a container
# (a container app next to the API). Build context is the repository root, like the .NET images.
#
# nginx serves the Vite bundle with SPA fallback (the OIDC /auth/callback route is client-side),
# proxies /api to the API on the same origin (so the browser needs no CORS), and renders
# /config.js at start-up from the container's environment, so the identity provider's realm is a
# deploy-time value rather than one baked into the bundle at build time (see src/lib/auth.ts).

FROM node:24-alpine@sha256:44fce696f38ba8c987c4db89954051000bbd05ffd8d8659ba15ed4a8602e937f AS build
WORKDIR /src
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# The unprivileged variant runs every process as uid 101, not only the workers. The bundle stays root-owned,
# so nginx can read it but never rewrite it; config.js, the one file rendered at start-up, gets its own directory.
FROM nginxinc/nginx-unprivileged:1.31-alpine@sha256:b9241c6e7b8e9a862f129d8d4199ab64b10390949a78bdd5603379b32c844083
USER root
COPY web/deploy/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY web/deploy/config.js.template /etc/nginx/templates/config.js.template
COPY --chmod=755 web/deploy/10-render-config.sh /docker-entrypoint.d/10-render-config.sh
COPY --from=build /src/dist /usr/share/nginx/html
# The base's digest trails Alpine's security fixes; CI's Trivy scan fails on a fixable HIGH.
RUN apk upgrade --no-cache && mkdir -p /usr/share/nginx/runtime && chown 101:101 /usr/share/nginx/runtime
USER 101
EXPOSE 8080
