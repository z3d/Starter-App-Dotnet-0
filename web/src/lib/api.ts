import createClient, { type Middleware } from 'openapi-fetch'
import { User } from 'oidc-client-ts'
import type { paths } from './api.generated'
import { oidcSettings, signInAgain, stepUp } from './auth'
import { build } from './build'
import { clearOutage, markStale, NetworkError, type Outage, reportOutage, setServiceNotice } from './connection'
import type { Full } from './types'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
  [extension: string]: unknown
}

// A validation problem's message is its first field error, which says what to change.
function firstValidationMessage(problem: ProblemDetails | undefined): string | undefined {
  const errors = problem?.errors
  if (!errors || typeof errors !== 'object') return undefined
  const first = Object.values(errors as Record<string, unknown>).flat()[0]
  return typeof first === 'string' ? first : undefined
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | undefined
  readonly correlationId: string | null

  constructor(status: number, problem: ProblemDetails | undefined, correlationId: string | null) {
    super(firstValidationMessage(problem) ?? problem?.detail ?? problem?.title ?? `Request failed with ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.correlationId = correlationId
  }

  get isValidation() {
    return this.status === 400 && !!this.problem?.errors
  }
}

function currentAccessToken(): string | undefined {
  const key = `oidc.user:${oidcSettings.authority}:${oidcSettings.client_id}`
  const raw = window.sessionStorage.getItem(key)
  if (!raw) return undefined
  const user = User.fromStorageString(raw)
  return user.expired ? undefined : user.access_token
}

export function newCorrelationId(): string {
  return crypto.randomUUID()
}

export const identity: Middleware = {
  onRequest({ request }) {
    const token = currentAccessToken()
    if (token && !request.headers.has('Authorization')) request.headers.set('Authorization', `Bearer ${token}`)
    if (!request.headers.has('X-Correlation-ID')) request.headers.set('X-Correlation-ID', newCorrelationId())
    request.headers.set('X-Client-Build', build)
    return request
  },
  onResponse({ request, response }) {
    if (response.status === 401 && request.headers.has('Authorization')) signInAgain()
    if (response.status === 403) stepUp(response.headers.get('WWW-Authenticate'))
    if (response.headers.get('X-Client-Reload') === 'true') markStale()
    const notice = response.headers.get('X-Service-Notice')
    setServiceNotice(notice ? decodeURIComponent(notice) : null)
    return response
  },
}

// Sized for a phone on a slow network: generous, but a call that hangs still ends.
export const timeouts = { readMs: 20_000, writeMs: 45_000 }
const gatewayFailures = new Set([502, 503, 504])

// A read is tried once more after a network failure or a gateway error; a write never is, since it may have landed.
// Whether the device is online is asked only after a call has failed, to explain the failure.
export async function resilientFetch(request: Request): Promise<Response> {
  const isRead = request.method === 'GET' || request.method === 'HEAD'
  const attempts = isRead ? 2 : 1
  for (let attempt = 1; ; attempt++) {
    const timeout = AbortSignal.timeout(isRead ? timeouts.readMs : timeouts.writeMs)
    try {
      const response = await globalThis.fetch(
        new Request(isRead ? request.clone() : request, { signal: AbortSignal.any([request.signal, timeout]) }),
      )
      if (gatewayFailures.has(response.status)) {
        if (attempt < attempts) continue
        reportOutage('unavailable')
      } else {
        clearOutage()
      }
      return response
    } catch (error) {
      if (request.signal.aborted) throw error
      if (attempt < attempts) continue
      const outage: Outage = timeout.aborted ? 'timeout' : navigator.onLine ? 'unreachable' : 'offline'
      reportOutage(outage)
      throw new NetworkError(outage)
    }
  }
}

// Same origin: the dev server and the image both proxy /api to the API. fetch is looked up per call,
// so a test's stand-in for it is the one used.
export const client = createClient<paths>({
  baseUrl: window.location.origin,
  fetch: (request) => resilientFetch(request),
})
client.use(identity)

// The data on success, typed as what the API actually sends (Full); an ApiError otherwise.
export async function call<T>(
  promise: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<Full<NonNullable<T>>> {
  const { data, error, response } = await promise
  if (error !== undefined || !response.ok) {
    const problem = error && typeof error === 'object' ? (error as ProblemDetails) : undefined
    throw new ApiError(response.status, problem, response.headers.get('X-Correlation-ID'))
  }
  return data as Full<NonNullable<T>>
}

export async function download(path: string): Promise<{ blob: Blob; fileName: string | null }> {
  const token = currentAccessToken()
  const response = await fetch(path, {
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), 'X-Correlation-ID': newCorrelationId() },
  })
  if (!response.ok) throw new ApiError(response.status, undefined, response.headers.get('X-Correlation-ID'))
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const fileName = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? null
  return { blob: await response.blob(), fileName }
}
