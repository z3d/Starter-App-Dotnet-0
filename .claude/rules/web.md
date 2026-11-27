---
paths:
  - "web/**"
---
# The web app

Moved from `AGENTS.md` unchanged.

- **The browser holds no token.** The API signs the person in (`/api/auth/login`) and keeps the tokens; the browser holds an `HttpOnly` session cookie. `web/src/lib/auth.ts` is the only code that starts a sign-in, a sign-out or a step-up, and the only place the session's CSRF token is kept, in memory; the API client's middleware (`src/lib/api.ts`) sends that token on every change and answers a 401, an ended session and the step-up 403. Nothing in the web app stores a token, reads the `Authorization` header or talks to the identity provider. The API is called only through `web/src/lib/api.ts`'s `client`, `openapi-fetch` typed by route over `web/src/lib/api.generated.ts` (`npm run gen:api` writes it from the running API's OpenAPI document; `web/e2e/run-e2e.sh` regenerates it against the API it boots and CI fails when the committed file differs), never through a hand-typed DTO or a hand-written fetch. The image knows no identity provider; the API's `Identity:Session` settings do (`docs/DECISIONS.md`, "The browser holds no token").
