import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionResponse } from '@/api/types'
import { createPinia, setActivePinia } from 'pinia'

const session = vi.fn<() => Promise<SessionResponse>>()
const logout = vi.fn<() => Promise<void>>()
vi.mock('@/api/endpoints', () => ({ api: { session: () => session(), logout: () => logout() } }))
const { useAuthStore } = await import('../auth')

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

  it('treats a failed session request as signed out', async () => {
    session.mockRejectedValue(new Error('network'))
    const auth = useAuthStore()
    await auth.ensureLoaded()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.csrfToken).toBeNull()
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
