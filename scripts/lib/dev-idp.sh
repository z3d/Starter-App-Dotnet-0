# Shared helpers for booting the dev Keycloak IdP in throwaway-stack scripts
# (tests/k6/run-perf.sh, dast/run-dast.sh) and minting access tokens from it.
#
# The realm is the same committed file the Aspire AppHost imports
# (src/StarterApp.AppHost/Realms/starterapp-realm.json), so scripted stacks and
# the local dev loop validate against identical claims: username-as-sub,
# tenant_id attribute -> tid, hardcoded amr incl. mfa, audience starterapp-api.
#
# Image digest matches the AppHost pin — bump both together.

DEV_IDP_IMAGE="quay.io/keycloak/keycloak:26.4@sha256:9409c59bdfb65dbffa20b11e6f18b8abb9281d480c7ca402f51ed3d5977e6007"
DEV_IDP_REALM="starterapp"
DEV_IDP_CLIENT_ID="starterapp-dev"
DEV_IDP_CLIENT_SECRET="local-dev-client-secret-not-a-secret"
DEV_IDP_SCOPES="customers:read customers:write orders:read orders:write products:read products:write"

# idp_start <container-name> <host-port> <repo-root>
# Boots Keycloak with the committed realm imported and waits for the realm
# endpoint to answer. Honors the caller's CONTAINER_CLI (run-dast.sh selects
# docker or podman; run-perf.sh is docker-only), defaulting to docker. Caller
# owns container cleanup ($CONTAINER_CLI rm -f <name>).
idp_start() {
  local container="$1" port="$2" repo_root="$3"

  "${CONTAINER_CLI:-docker}" run -d --name "$container" \
    -e KC_BOOTSTRAP_ADMIN_USERNAME=admin \
    -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
    -v "${repo_root}/src/StarterApp.AppHost/Realms:/opt/keycloak/data/import:ro" \
    -p "${port}:8080" \
    "$DEV_IDP_IMAGE" start-dev --import-realm >/dev/null

  local ready=0
  for _ in $(seq 1 60); do
    if curl -fsS "http://localhost:${port}/realms/${DEV_IDP_REALM}" >/dev/null 2>&1; then
      ready=1
      break
    fi
    sleep 2
  done
  [[ "$ready" == "1" ]] || return 1
}

# idp_token <idp-base-url> <username> <password> [scopes]
# Prints an access token minted via the password grant. Scopes default to the
# full resource-scope set (they are optional client scopes in the realm, so
# they must be requested explicitly).
idp_token() {
  local base_url="$1" username="$2" password="$3" scopes="${4:-$DEV_IDP_SCOPES}"

  curl -fsS "${base_url}/realms/${DEV_IDP_REALM}/protocol/openid-connect/token" \
    --data-urlencode "grant_type=password" \
    --data-urlencode "client_id=${DEV_IDP_CLIENT_ID}" \
    --data-urlencode "client_secret=${DEV_IDP_CLIENT_SECRET}" \
    --data-urlencode "username=${username}" \
    --data-urlencode "password=${password}" \
    --data-urlencode "scope=${scopes}" \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])'
}
