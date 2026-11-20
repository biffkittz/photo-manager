import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, CSRF_PROBLEM_TYPE, apiFetch, configureHttp } from '../http'

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('apiFetch', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    configureHttp({ csrfToken: () => null, unauthorized: () => {} })
  })

  it('sends the CSRF token on mutating requests only', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    configureHttp({ csrfToken: () => 'token-123', unauthorized: () => {} })

    await apiFetch('/api/photos')
    await apiFetch('/api/auth/logout', { method: 'POST' })

    const getHeaders = fetchMock.mock.calls[0]![1]!.headers as Headers
    const postHeaders = fetchMock.mock.calls[1]![1]!.headers as Headers
    expect(getHeaders.has('X-CSRF-TOKEN')).toBe(false)
    expect(postHeaders.get('X-CSRF-TOKEN')).toBe('token-123')
    expect(fetchMock.mock.calls[1]![1]!.credentials).toBe('same-origin')
  })

  it('maps problem details to a user-safe ApiError', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(
        jsonResponse(400, {
          title: 'Bad',
          errors: { q: ['Search text is limited to 200 characters.'] },
        }),
      ),
    )

    const error = await apiFetch('/api/photos').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).message).toBe('Search text is limited to 200 characters.')
    expect((error as ApiError).isClientError).toBe(true)
  })

  it('fetches a fresh CSRF token and retries once when the token was stale', async () => {
    let token = 'stale'
    const refreshCsrf = vi.fn<() => Promise<void>>(() => {
      token = 'fresh'
      return Promise.resolve()
    })
    configureHttp({ csrfToken: () => token, unauthorized: () => {}, refreshCsrf })
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        jsonResponse(403, { type: CSRF_PROBLEM_TYPE, title: 'Missing or invalid CSRF token.' }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await apiFetch('/api/auth/logout', { method: 'POST' })

    expect(refreshCsrf).toHaveBeenCalledOnce()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect((fetchMock.mock.calls[1]![1]!.headers as Headers).get('X-CSRF-TOKEN')).toBe('fresh')
  })

  it('does not retry other 403 responses', async () => {
    const refreshCsrf = vi.fn<() => Promise<void>>(() => Promise.resolve())
    configureHttp({ csrfToken: () => 'token', unauthorized: () => {}, refreshCsrf })
    const fetchMock = vi
      .fn<typeof fetch>()
      .mockResolvedValue(jsonResponse(403, { title: 'Forbidden' }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/jobs', { method: 'POST', body: {} })).rejects.toThrow('Forbidden')
    expect(refreshCsrf).not.toHaveBeenCalled()
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('notifies the auth layer when the session has expired', async () => {
    const unauthorized = vi.fn<() => void>()
    configureHttp({ csrfToken: () => null, unauthorized })
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 401 })),
    )

    await expect(apiFetch('/api/photos')).rejects.toThrow('Your session has expired')
    expect(unauthorized).toHaveBeenCalledOnce()
  })
})
