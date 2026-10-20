import { User } from 'oidc-client-ts'
import { oidcSettings, signInAgain, stepUp } from './auth'

// Minimal typed fetch wrapper. Every request carries the current access token and a correlation id
// (the API echoes X-Correlation-ID, which is the support handle when something goes wrong).
// Non-2xx responses surface as ApiError carrying the RFC 9457 ProblemDetails body the API returns.

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

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
  correlationId?: string
}

export async function api<T = unknown>(path: string, options: RequestOptions = {}): Promise<T> {
  const { body, correlationId = newCorrelationId(), headers, ...init } = options
  const token = currentAccessToken()

  const response = await fetch(path.startsWith('/') ? path : `/${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      'X-Correlation-ID': correlationId,
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    // The session is gone, or the API wants the second factor: the provider sorts both out.
    if (response.status === 401 && token) signInAgain()
    if (response.status === 403) stepUp(response.headers.get('WWW-Authenticate'))
    let problem: ProblemDetails | undefined
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      problem = undefined
    }
    throw new ApiError(response.status, problem, response.headers.get('X-Correlation-ID'))
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const http = {
  get: <T>(path: string, options?: RequestOptions) => api<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) =>
    api<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) => api<T>(path, { ...options, method: 'PUT', body }),
  delete: <T>(path: string, options?: RequestOptions) => api<T>(path, { ...options, method: 'DELETE' }),
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
