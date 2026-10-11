# StarterApp web

The TypeScript workspace for the template's client surface: the client half of the identity seam
the API's bearer validation is the server half of, and one sample page over the sample domain.

| Package | What it is |
|---|---|
| `packages/api-client` | The API's OpenAPI document (`openapi.json`) and the typed client generated from it (`openapi-typescript`, `openapi-fetch`), with the correlation id and the problem-details error on every call. |
| `apps/web` | The web app: a Vite single-page application in React and TypeScript. `src/lib/auth.ts` signs the person in, `src/lib/api.ts` sends their token and answers the API's challenges, `src/sample/` is the walkthrough page a derived project removes with the sample. |

## Running it

```bash
cd web
pnpm install
pnpm dev                            # http://localhost:5173, the API at http://localhost:5164
```

The app calls the API at relative `/api/...` paths. In development the Vite server proxies those
to the API (`VITE_API_URL`, default `http://localhost:5164`, the API's own launch profile) and the
app signs you in through the dev Keycloak's `starterapp` realm on `:8090` (`VITE_OIDC_AUTHORITY`
overrides it), which the AppHost or `scripts/dev/keycloak.sh` serves there. The users are
`dev-user` and `password-only`, both with `dev-password`; the second carries no second factor,
so every write it makes answers the step-up challenge.

The app signs the person in with OpenID Connect (authorization code with PKCE through
`oidc-client-ts`, `src/lib/auth.ts`) as the realm's public `starterapp-web` client and sends their
access token on every request. A 401 sends them back to sign in; a 403 carrying the API's step-up
challenge (RFC 9470) sends them to sign in again with the second factor, at the `acr_values` the
challenge names. Signing in again with fewer scopes shows the API refusing a write from the token
alone.

Deployed, the container renders `/config.js` from `OIDC_AUTHORITY`, `OIDC_CLIENT_ID` and
`OIDC_SCOPE` (`apps/web/deploy`), which name the real identity provider; there is no other way the
app knows who you are. An Auth0 tenant also takes `OIDC_AUDIENCE` (the API identifier, without
which Auth0 issues an opaque token) and `OIDC_ORGANIZATION`; a Keycloak realm needs neither.
`VITE_OIDC_AUDIENCE` and `VITE_OIDC_ORGANIZATION` are the build-time equivalents.

## The generated client

`packages/api-client/openapi.json` is what the API serves at `/openapi/v1.json` in Development,
and `src/schema.d.ts` is generated from it. When an endpoint changes:

```bash
pnpm export                         # fetch the running API's document (OPENAPI_URL overrides the default) and regenerate
pnpm generate                       # regenerate from the committed document alone
```

CI fails when `schema.d.ts` is not what `openapi.json` generates, and the browser-test job fails
when `openapi.json` is not what the API serves, so a changed endpoint cannot land without its
client.

## Testing

```bash
pnpm test                                   # unit tests (Vitest, jsdom): the identity helper, the client's middleware, the deploy templates
pnpm e2e                                    # browser tests (Playwright) against the rig on :5164 / :8090 (BASE_URL, API_URL, IDP_URL override)
e2e/run-e2e.sh                              # the same tests against a stack the script boots: PostgreSQL, the dev Keycloak, the migrator, the API, the dev server
e2e/run-e2e.sh --grep "step-up"             # arguments go to `playwright test`
E2E_DEPLOYED=1 e2e/run-e2e.sh               # the deployed shape: the production image (apps/web/Dockerfile) behind its own nginx, the API at its normal rate limit
pnpm exec playwright install chromium       # once, for the browser
```

The browser tests drive the app as a person does, against a live API and the dev Keycloak:
signing in through the identity provider, the walkthrough (a product, a customer, an order, and
the refusal when the person signs in again without `orders:write`), and the step-up challenge as
`password-only`. The fixtures in `e2e/lib/fixtures.ts` sign a test in by putting the person's
tokens where `oidc-client-ts` keeps them, so only the sign-in spec goes through Keycloak's own
page. The step-up spec runs to the end with the second factor stood in for: the dev realm's `amr`
is a user attribute, so the test raises it through Keycloak's admin API after the challenge and
puts it back; Keycloak demanding and checking a real second factor is not covered.

CI runs both (`web` and `web-e2e` in `.github/workflows/ci.yml`); the browser job boots its stack
with `e2e/run-e2e.sh`, the way the DAST and k6 harnesses boot theirs, and builds the image for the
deployed shape too.

## The image

`apps/web/Dockerfile` builds the bundle and serves it from the unprivileged nginx image with SPA
fallback, `/api` proxied to `API_UPSTREAM` on the same origin (so the browser needs no CORS), and
`/config.js` rendered from the environment at start-up. CI builds and scans it with the three .NET
images, and `publish-images.yml` pushes it to GHCR as `web`.
