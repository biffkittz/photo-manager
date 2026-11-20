import type { ProblemDetails } from './types'

/** An HTTP error from the API, carrying a user-safe message. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly problem?: ProblemDetails,
  ) {
    super(message)
    this.name = 'ApiError'
  }

  /** True for 4xx responses, which retrying will not fix. */
  get isClientError(): boolean {
    return this.status >= 400 && this.status < 500
  }
}

type CsrfTokenProvider = () => string | null
type UnauthorizedHandler = () => void
type CsrfRefresher = () => Promise<void>

/** Problem type the API uses when it refuses a request for a missing or stale anti-CSRF token. */
export const CSRF_PROBLEM_TYPE = 'urn:photo-manager:problem:csrf'

let csrfTokenProvider: CsrfTokenProvider = () => null
let onUnauthorized: UnauthorizedHandler = () => {}
let csrfRefresher: CsrfRefresher = () => Promise.resolve()
let pendingCsrfRefresh: Promise<void> | null = null

/**
 * Wires the HTTP layer to the auth store without a circular import: the store supplies the CSRF token
 * (kept in memory only, never in localStorage), fetches a fresh one when the API rejects a stale one, and
 * reacts when the session has expired.
 */
export function configureHttp(options: {
  csrfToken: CsrfTokenProvider
  unauthorized: UnauthorizedHandler
  refreshCsrf?: CsrfRefresher
}) {
  csrfTokenProvider = options.csrfToken
  onUnauthorized = options.unauthorized
  csrfRefresher = options.refreshCsrf ?? (() => Promise.resolve())
}

/** Fetches a fresh anti-CSRF token; concurrent callers share one request. */
export function refreshCsrfToken(): Promise<void> {
  pendingCsrfRefresh ??= csrfRefresher().finally(() => {
    pendingCsrfRefresh = null
  })
  return pendingCsrfRefresh
}

/** True when the API refused a request because its anti-CSRF token was missing or stale. */
export function isCsrfProblem(problem: unknown): boolean {
  return (
    typeof problem === 'object' &&
    problem !== null &&
    (problem as ProblemDetails).type === CSRF_PROBLEM_TYPE
  )
}

/** Returns the current CSRF token for callers that build requests themselves (XHR uploads). */
export function currentCsrfToken(): string | null {
  return csrfTokenProvider()
}

/** Reports an expired session from requests made outside apiFetch (XHR uploads, rendition polling). */
export function notifyUnauthorized(): void {
  onUnauthorized()
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE'
  body?: unknown
  signal?: AbortSignal
}

/**
 * Same-origin JSON fetch. Cookies are sent automatically (credentials: 'same-origin'); the session cookie is
 * HttpOnly, so scripts never see it. Mutating requests carry the anti-CSRF header.
 */
export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(path, options)
  if (
    response.status === 403 &&
    (options.method ?? 'GET') !== 'GET' &&
    (await isCsrfRejection(response))
  ) {
    // The anti-CSRF token went stale (for example after signing in again in another tab): fetch a fresh one and
    // retry once. A second rejection is reported like any other error.
    await refreshCsrfToken()
    response = await send(path, options)
  }

  if (response.status === 401) {
    onUnauthorized()
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  if (
    response.status === 204 ||
    (response.status === 202 && response.headers.get('Content-Length') === '0')
  ) {
    return undefined as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

function send(path: string, options: RequestOptions): Promise<Response> {
  const method = options.method ?? 'GET'
  const headers = new Headers({ Accept: 'application/json' })
  if (options.body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }

  if (method !== 'GET') {
    const token = csrfTokenProvider()
    if (token) {
      headers.set('X-CSRF-TOKEN', token)
    }
  }

  return fetch(path, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    credentials: 'same-origin',
    signal: options.signal,
  })
}

async function isCsrfRejection(response: Response): Promise<boolean> {
  try {
    return isCsrfProblem(await response.clone().json())
  } catch {
    return false
  }
}

/** Converts an error response into an {@link ApiError} with the most helpful user-safe message available. */
export async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails | undefined
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    problem = undefined
  }

  const validation = problem?.errors ? Object.values(problem.errors).flat().join(' ') : ''
  const message = validation || problem?.title || defaultMessage(response.status)
  return new ApiError(response.status, message, problem)
}

function defaultMessage(status: number): string {
  switch (status) {
    case 401:
      return 'Your session has expired. Please sign in again.'
    case 403:
      return 'You do not have permission to do that.'
    case 404:
      return 'That item could not be found.'
    case 429:
      return 'Too many requests. Please wait a moment and try again.'
    default:
      return status >= 500
        ? 'The server had a problem. Please try again.'
        : 'The request could not be completed.'
  }
}
