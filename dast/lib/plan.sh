#!/usr/bin/env bash

# Return host/container base URLs. Query, fragment, credentials and malformed
# escapes are not meaningful for an API base URL and must never change the target.
dast_target_urls() {
  jq -cen --arg target "$1" '
    $target
    | capture("^(?<scheme>https?)://(?<host>[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?|\\[[0-9A-Fa-f:.]+\\])(?<port>:[0-9]+)?(?<path>/[A-Za-z0-9._~!$&\u0027()*+,;=:@%/-]*)?$"; "i")
    | select((.port == null) or ((.port[1:] | tonumber) >= 1 and (.port[1:] | tonumber) <= 65535))
    | select((.path // "" | gsub("%[0-9a-fA-F]{2}"; "") | contains("%")) | not)
    | .scheme |= ascii_downcase
    | .path = ((.path // "") | sub("/+$"; ""))
    | .port = (.port // "")
    | .host as $host
    | (.host | ascii_downcase) as $lower
    | ($lower == "localhost" or $lower == "localhost." or $lower == "[::1]"
       or ($lower | test("^127(?:\\.[0-9]{1,3}){3}$"))) as $loopback
    | {host: (.scheme + "://" + $host + .port + .path),
       scanner: (.scheme + "://" + (if $loopback then "host.docker.internal" else $host end) + .port + .path)}
  '
}

# JSON string encoding is valid YAML scalar encoding. Escape regex metacharacters
# separately before encoding the regex fields, so a hostname/path stays literal.
dast_render_plan() {
  jq -Rrs --arg base "$1" --arg token "$DAST_TOKEN" '
    def regex_literal:
      split("") | map(if . == "." or . == "\\" or . == "+" or . == "*" or . == "?"
        or . == "[" or . == "]" or . == "(" or . == ")" or . == "{" or . == "}"
        or . == "^" or . == "$" or . == "|" then "\\" + . else . end) | join("");
    ($base | regex_literal) as $regex
    | {"__ZAP_BASE_URL__": $base,
       "__ZAP_OPENAPI_URL__": ($base + "/openapi/v1.json"),
       "__ZAP_API_REGEX__": ("^" + $regex + "/api/.*$"),
       "__ZAP_HEALTH_REGEX__": ("^" + $regex + "/health.*$"),
       "__ZAP_ALIVE_REGEX__": ("^" + $regex + "/alive$"),
       "__ZAP_SCALAR_REGEX__": ("^" + $regex + "/scalar.*$"),
       "__ZAP_OPENAPI_REGEX__": ("^" + $regex + "/openapi.*$"),
       "__ZAP_PAGINATION_REGEX__": ("^" + $regex + "/api/v1/(products|customers|orders/customer/[^/?]+|orders/status/[^/?]+)/?(\\?.*)?$"),
       "__DAST_AUTH_HEADER__": ("Bearer " + $token)} as $values
    | gsub("(?<placeholder>__ZAP_[A-Z_]+__|__DAST_AUTH_HEADER__)"; $values[.placeholder] | tojson)
  ' "$2"
}
