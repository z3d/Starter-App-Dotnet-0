#!/bin/sh
# nginx's entrypoint renders /etc/nginx/templates/*.template into /etc/nginx/conf.d; this renders
# config.js into the one directory nginx's user may write, beside the read-only web root.
set -eu
: "${OIDC_CLIENT_ID:=starterapp-web}"
: "${OIDC_SCOPE:=openid products:read products:write customers:read customers:write orders:read orders:write}"
# Auth0 only: the API identifier and the Organization; empty for a Keycloak realm.
: "${OIDC_AUDIENCE:=}"
: "${OIDC_ORGANIZATION:=}"
export OIDC_CLIENT_ID OIDC_SCOPE OIDC_AUDIENCE OIDC_ORGANIZATION
envsubst '${OIDC_AUTHORITY} ${OIDC_CLIENT_ID} ${OIDC_SCOPE} ${OIDC_AUDIENCE} ${OIDC_ORGANIZATION}' \
  < /etc/nginx/templates/config.js.template > /usr/share/nginx/runtime/config.js
