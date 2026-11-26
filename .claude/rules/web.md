---
paths:
  - "web/**"
---
# The web app

Moved from `AGENTS.md` unchanged.

- **The web app is the client half of that seam, and `web/src/lib/auth.ts` is the only code that holds the token.** It signs the person in with the authorization code flow and PKCE as a public client, the API client's middleware (`src/lib/api.ts`) sends the token and answers a 401 and the step-up 403, and nothing else reads `sessionStorage` or the `Authorization` header. The API is called only through `web/src/lib/api.ts`'s `client`, `openapi-fetch` typed by route over `web/src/lib/api.generated.ts` (`npm run gen:api` writes it from the running API's OpenAPI document; `web/e2e/run-e2e.sh` regenerates it against the API it boots and CI fails when the committed file differs), never through a hand-typed DTO or a hand-written fetch. A hosted instance learns its identity provider from `/config.js` rendered at start-up, never from a value baked into the bundle.
