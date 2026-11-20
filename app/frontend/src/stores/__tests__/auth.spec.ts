import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionResponse } from '@/api/types'
import { ApiError } from '@/api/http'
import { createPinia, setActivePinia } from 'pinia'

const session = vi.fn<() => Promise<SessionResponse>>()
const logout = vi.fn<() => Promise<void>>()
vi.mock('@/api/endpoints', () => ({ api: { session: () => session(), logout: () => logout() } }))
const { useAuthStore, SESSION_CHANNEL } = await import('../auth')

const signedIn: SessionResponse = {
  authenticated: true,
  csrfToken: 't',
  user: { gitHubId: 1, login: 'octo', name: null, avatarUrl: null, role: 'Member' },
}

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    session.mockReset()
    logout.mockReset()
  })

  it('loads the session once even when called concurrently', async () => {
    session.mockResolvedValue({
      authenticated: true,
      csrfToken: 't',
      user: { gitHubId: 1, login: 'octo', name: null, avatarUrl: null, role: 'Admin' },
    })
    const auth = useAuthStore()

    await Promise.all([auth.ensureLoaded(), auth.ensureLoaded()])

    expect(session).toHaveBeenCalledOnce()
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.isAdmin).toBe(true)
    expect(auth.displayName).toBe('octo')
  })

  it('concludes nothing from a session check that got no answer, and asks again next time', async () => {
    session.mockRejectedValueOnce(new Error('network'))
    const auth = useAuthStore()

    await auth.ensureLoaded()
    expect(auth.loaded).toBe(false)
    expect(auth.unreachable).toBe(true)

    session.mockResolvedValueOnce(signedIn)
    await auth.ensureLoaded()
    expect(auth.loaded).toBe(true)
    expect(auth.unreachable).toBe(false)
    expect(auth.isAuthenticated).toBe(true)
  })

  it('does not sign a user out because a later check failed', async () => {
    session.mockResolvedValueOnce(signedIn).mockRejectedValueOnce(new Error('offline'))
    const auth = useAuthStore()
    await auth.ensureLoaded()

    await auth.refresh()

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.csrfToken).toBe('t')
  })

  it('ends the session locally when the server says it has ended', async () => {
    session
      .mockResolvedValueOnce(signedIn)
      .mockResolvedValueOnce({ authenticated: false, csrfToken: null, user: null })
    const auth = useAuthStore()
    const cleanup = vi.fn<() => void>()
    auth.onSessionEnded(cleanup)
    await auth.ensureLoaded()

    await auth.refresh()

    expect(auth.isAuthenticated).toBe(false)
    expect(cleanup).toHaveBeenCalledOnce()
  })

  it('treats a 401 on sign-out as already signed out', async () => {
    session.mockResolvedValue(signedIn)
    logout.mockRejectedValue(new ApiError(401, 'Your session has expired. Please sign in again.'))
    const auth = useAuthStore()
    await auth.ensureLoaded()

    await expect(auth.logout()).resolves.toBeUndefined()
    expect(auth.user).toBeNull()
  })

  it('ends the session in every tab when it ends in one', async () => {
    session.mockResolvedValue(signedIn)
    const auth = useAuthStore()
    const cleanup = vi.fn<() => void>()
    auth.onSessionEnded(cleanup)
    await auth.ensureLoaded()

    // Another tab of the app signs out.
    const otherTab = new BroadcastChannel(SESSION_CHANNEL)
    otherTab.postMessage('session-ended')
    otherTab.close()

    await vi.waitFor(() => expect(auth.isAuthenticated).toBe(false))
    expect(cleanup).toHaveBeenCalledOnce()
  })

  it('tells the other tabs when the session ends here', async () => {
    session.mockResolvedValue(signedIn)
    const auth = useAuthStore()
    await auth.ensureLoaded()
    const otherTab = new BroadcastChannel(SESSION_CHANNEL)
    const received = vi.fn<(data: unknown) => void>()
    otherTab.addEventListener('message', (event: MessageEvent) => received(event.data))

    auth.clear()

    await vi.waitFor(() => expect(received).toHaveBeenCalledWith('session-ended'))
    otherTab.close()
  })

  it('clears local state on logout even if the request fails', async () => {
    session.mockResolvedValue({
      authenticated: true,
      csrfToken: 't',
      user: { gitHubId: 1, login: 'octo', name: null, avatarUrl: null, role: 'Member' },
    })
    logout.mockRejectedValue(new Error('offline'))
    const auth = useAuthStore()
    await auth.ensureLoaded()

    await expect(auth.logout()).rejects.toThrow('offline')
    expect(auth.user).toBeNull()
  })

  it('runs session-ended cleanup only when a signed-in session ends', async () => {
    session.mockResolvedValue({
      authenticated: true,
      csrfToken: 't',
      user: { gitHubId: 1, login: 'octo', name: null, avatarUrl: null, role: 'Member' },
    })
    const auth = useAuthStore()
    const cleanup = vi.fn<() => void>()
    auth.onSessionEnded(cleanup)

    auth.clear() // not signed in yet: nothing to clean up
    expect(cleanup).not.toHaveBeenCalled()

    await auth.ensureLoaded()
    auth.clear()
    expect(cleanup).toHaveBeenCalledOnce()
  })
})
