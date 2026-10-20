import { afterEach, describe, expect, it, vi } from 'vitest'

// The provider's redirects are stood in for; the client's own behaviour is what is tested.
const signInAgain = vi.fn()
const stepUp = vi.fn(() => true)
vi.mock('./auth', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./auth')>()),
  signInAgain,
  stepUp,
}))
const { ApiError, call, client } = await import('./api')
type ApiError = InstanceType<typeof ApiError>
const { oidcSettings } = await import('./auth')

// A signed-in session, the way oidc-client-ts keeps one.
function signIn(token = 'token-123') {
  window.sessionStorage.setItem(
    `oidc.user:${oidcSettings.authority}:${oidcSettings.client_id}`,
    JSON.stringify({
      access_token: token,
      token_type: 'Bearer',
      scope: 'openid',
      profile: { sub: 'dev-user' },
      expires_at: Math.floor(Date.now() / 1000) + 600,
    }),
  )
}

function jsonResponse(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  })
}

// The request openapi-fetch hands to fetch.
const sent = (mock: { mock: { calls: unknown[][] } }, i = 0) => mock.mock.calls[i]![0] as Request

describe('API client', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    signInAgain.mockClear()
    stepUp.mockClear()
    window.sessionStorage.clear()
  })

  it("sends the signed-in person's token, and not an expired one", async () => {
    signIn()
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => jsonResponse(200, []))
    await call(client.GET('/api/v1/customers'))
    expect(sent(fetchMock).headers.get('Authorization')).toBe('Bearer token-123')

    window.sessionStorage.setItem(
      `oidc.user:${oidcSettings.authority}:${oidcSettings.client_id}`,
      JSON.stringify({
        access_token: 'stale',
        token_type: 'Bearer',
        scope: 'openid',
        profile: {},
        expires_at: Math.floor(Date.now() / 1000) - 60,
      }),
    )
    await call(client.GET('/api/v1/customers'))
    expect(sent(fetchMock, 1).headers.get('Authorization')).toBeNull()
  })

  it('signs the person in again when their token is refused', async () => {
    signIn()
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(401, { title: 'Unauthorized' }))
    await expect(call(client.GET('/api/v1/customers'))).rejects.toBeInstanceOf(ApiError)
    expect(signInAgain).toHaveBeenCalled()
  })

  it('leaves a signed-out 401 alone: the layout route is already sending them to sign in', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(401, { title: 'Unauthorized' }))
    await expect(call(client.GET('/api/v1/customers'))).rejects.toBeInstanceOf(ApiError)
    expect(signInAgain).not.toHaveBeenCalled()
  })

  it('asks for the second factor when a write is refused with the step-up challenge', async () => {
    signIn()
    const challenge = 'Bearer error="insufficient_user_authentication", acr_values="urn:starterapp:loa:mfa"'
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(403, { title: 'Forbidden' }, { 'WWW-Authenticate': challenge }),
    )
    await expect(
      call(client.POST('/api/v1/customers', { body: { name: 'x', email: 'x@example.test' } })),
    ).rejects.toBeInstanceOf(ApiError)
    expect(stepUp).toHaveBeenCalledWith(challenge)
  })

  it('sends JSON, a correlation id, and no Authorization header when signed out', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(201, { id: 1, name: 'x' }))

    const result = await call(client.POST('/api/v1/customers', { body: { name: 'x', email: 'x@example.test' } }))

    expect(result).toEqual({ id: 1, name: 'x' })
    const request = sent(fetchMock)
    expect(new URL(request.url).pathname).toBe('/api/v1/customers')
    expect(request.headers.get('X-Correlation-ID')).toMatch(/^[0-9a-f-]{36}$/)
    expect(request.headers.get('Content-Type')).toBe('application/json')
    expect(request.headers.get('Authorization')).toBeNull()
    expect(await request.json()).toEqual({ name: 'x', email: 'x@example.test' })
  })

  it('surfaces ProblemDetails as ApiError with the echoed correlation id', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      jsonResponse(
        400,
        { title: 'Validation failed', status: 400, errors: { Name: ['Required'] } },
        { 'X-Correlation-ID': 'corr-2' },
      ),
    )

    const error = await call(client.GET('/api/v1/customers')).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(400)
    expect(apiError.isValidation).toBe(true)
    expect(apiError.problem?.errors).toEqual({ Name: ['Required'] })
    expect(apiError.correlationId).toBe('corr-2')
    expect(apiError.message).toBe('Required')
  })

  it('returns undefined for 204 responses', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))
    await expect(call(client.DELETE('/api/v1/products/{id}', { params: { path: { id: 1 } } }))).resolves.toBeUndefined()
  })
})
