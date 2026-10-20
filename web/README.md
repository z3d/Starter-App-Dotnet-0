# The StarterApp web app

Vite and React, hosted by the AppHost (`dotnet run --project dev/StarterApp.AppHost`, then http://localhost:5173, `dev-user` / `dev-password`). The app signs the person in with OpenID Connect (authorization code with PKCE through `oidc-client-ts`, `src/lib/auth.ts`) against `VITE_OIDC_AUTHORITY`, the AppHost's dev Keycloak realm, and sends their access token on every request; the API validates it itself. A 401 on a signed-in session sends them to sign in again, and a 403 carrying the API's step-up challenge sends them to sign in again with the second factor, both back to the page they were on. A hosted instance takes the identity provider from `/config.js`, rendered at container start (`deploy/`), so the realm is a deploy-time value.

The API is called only through `src/lib/api.ts`: `call(client.GET('/api/v1/orders/{id}', { params: { path: { id } } }))`. The client is `openapi-fetch` over `src/lib/api.generated.ts`, which `npm run gen:api` writes from the running API's OpenAPI document, so every call is typed by its route: a route that does not exist, a wrong parameter or a wrong body fails to compile, and the response type follows from the route. `call` returns the data typed as what the API sends (`Full`, `src/lib/types.ts`) or throws `ApiError` with the problem details. The generated file is committed; `e2e/run-e2e.sh` regenerates it against the API it boots and, in CI, fails when the committed file differs, so a client that has drifted from the API is caught.

`src/sample/` is the sample page over the Customer/Product/Order sample: it creates each through the API and shows what the API answers, so the identity seam can be seen end to end. A derived project removes it with the sample domain (`docs/DERIVATION-PRUNING.md`) and keeps the shell, the identity helper, the API client, the primitives and the fixtures.

```bash
npm run dev            # the dev server alone, proxying /api to VITE_API_URL (default https://localhost:7286)
npm run typecheck && npm run lint && npm run format:check && npm run build
npm run gen:api        # regenerate src/lib/api.generated.ts from the running API (OPENAPI_URL overrides the default)
```

## Testing

```bash
npm test                                   # unit and component tests (Vitest, jsdom): src/**/*.test.ts(x)
npm run e2e                                # browser tests (Playwright) against the rig on :7286 / :8090 / :5183 (BASE_URL, API_URL, IDP_URL override)
e2e/run-e2e.sh                             # the same tests against a stack the script boots: PostgreSQL, the dev Keycloak, the migrator, the API, the dev server
e2e/run-e2e.sh --grep "sign"               # arguments go to `playwright test`
E2E_DEPLOYED=1 e2e/run-e2e.sh              # the nginx image (Dockerfile) in front of the same stack, and the e2e/deployed project
npx playwright install chromium            # once, for the browser
```

The unit tests cover the HTTP client and the identity helpers (`src/lib/api.ts`, `auth.ts`) with `fetch` and the OIDC library stood in for, the component vocabulary (`src/components/ui`), the command palette, the runtime config (`src/test/deploy.test.ts` checks every value `auth.ts` reads is rendered and exported), and two scans of the source: `spelling.test.ts` fails on American spellings in text a person reads, and `day-from-utc.test.ts` fails on a calendar day cut from a UTC timestamp. The browser tests (`e2e/tests`) drive the app against a live API and the dev Keycloak: signing in through the identity provider, the step-up challenge (as `password-only`, whose token carries no second factor; the realm's `amr` attribute stands in for the factor through the admin API), the rail, the palette, and the sample page's product, customer and order with a refusal decided by the token's scopes. The fixtures in `e2e/lib/fixtures.ts` sign a test in by putting the person's tokens where `oidc-client-ts` keeps them, so only the sign-in spec goes through Keycloak's own page. CI runs both (`web` and `web-e2e` in `.github/workflows/ci.yml`); the browser job boots its stack with `e2e/run-e2e.sh`, then again with `E2E_DEPLOYED=1` against the image.

## Recording the walkthrough

`docs/media/starterapp-walkthrough.mp4` is a narrated recording of the sample: sign in, create a product and a customer, place and confirm an order. A derived project replaces the story with its own and re-records it after every milestone (`AGENTS.md`); the video is committed, so the repository always carries the current one.

The narration lines live at the top of `e2e/walkthrough.mjs`, one per scene, and are spoken by Microsoft's Australian neural voices through `edge-tts` (`pip install --upgrade edge-tts`, 7.3 or later: the service refuses an older version with a 403 and the recording comes out silent; it calls the Edge read-aloud service, so it needs the network). `e2e/lib/narration.mjs` synthesises each line before the browser opens, holds every scene at least as long as its line, and mixes the lines onto the video at the moment each scene began (ffmpeg on the path; without it the silent `.webm` stays). `NARRATOR` picks the voice (`en-AU-NatashaNeural` by default, `en-AU-WilliamMultilingualNeural` is the other Australian one); `NARRATOR=none` records silently. When a scene changes, change its line.

```bash
e2e/run-e2e.sh --walkthrough               # boots the stack on its own ports, records, tears down
BASE_URL=http://localhost:5173 node e2e/walkthrough.mjs   # against the running AppHost rig
```

Each scene is a few lines of Playwright, paced with `pause()` so a viewer can follow; use the same `data-testid`s and names the browser tests use, and add a flow to `e2e/tests` first so the story never drives something untested. `e2e/screenshots.mjs` is the still-image companion (`BASE_URL=http://localhost:5173 node e2e/screenshots.mjs` writes every screen in light, dark and mobile to `e2e/shots/`).
