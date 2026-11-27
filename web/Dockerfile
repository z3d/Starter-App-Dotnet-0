# The web app as a static site behind nginx, for environments that host it as a container
# (a container app next to the API). Build context is the repository root, like the .NET images.
#
# nginx serves the Vite bundle with SPA fallback and proxies /api to the API on the same origin, so
# the browser needs no CORS and the session cookie is the site's own. Sign-in runs through the API's
# /api/auth routes, so the image knows nothing of the identity provider.

FROM node:24-alpine@sha256:44fce696f38ba8c987c4db89954051000bbd05ffd8d8659ba15ed4a8602e937f AS build
WORKDIR /src
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# The commit the image is built from; the bundle sends it on every call, so the API can tell a stale tab to reload.
ARG BUILD=dev
RUN VITE_BUILD="$BUILD" npm run build

# The official image, made to run every process as uid 101 here (no user directive, the pid file and the temporary
# paths under /tmp, conf.d writable for the rendered template). nginxinc/nginx-unprivileged did this for us but was
# published late: it stood at 1.31.7 on 25 November 2026 while nginx's fixes were in 1.31.9 and 1.31.10. The bundle
# stays root-owned, so nginx can read it but never rewrite it.
FROM nginx:1.31.10-alpine@sha256:aceb952b920393b7d0bc7b978927fc283370bea89fb082c6fef0ce18c60f96a9
USER root
COPY web/deploy/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY web/deploy/security-headers.conf /etc/nginx/security-headers.conf
COPY --from=build /src/dist /usr/share/nginx/html
# The base's digest trails Alpine's security fixes; CI's Trivy scan fails on a fixable HIGH.
RUN apk upgrade --no-cache \
  && sed -i -e '/^user /d' -e 's#^pid .*#pid /tmp/nginx.pid;#' \
       -e 's#^http {#http {\n    client_body_temp_path /tmp/client_temp;\n    proxy_temp_path /tmp/proxy_temp;\n    fastcgi_temp_path /tmp/fastcgi_temp;\n    uwsgi_temp_path /tmp/uwsgi_temp;\n    scgi_temp_path /tmp/scgi_temp;#' /etc/nginx/nginx.conf \
  && chown -R 101:101 /etc/nginx/conf.d
USER 101
EXPOSE 8080
