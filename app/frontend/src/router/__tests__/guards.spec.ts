import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { SessionResponse } from '@/api/types'
import { createPinia, setActivePinia } from 'pinia'

const session = vi.fn<() => Promise<SessionResponse>>()
vi.mock('@/api/endpoints', () => ({ api: { session: () => session() }, urls: {} }))

describe('router guards', () => {
  beforeEach(() => {
    vi.resetModules()
    setActivePinia(createPinia())
  })

  it('sends anonymous users to login with a return URL', async () => {
    session.mockResolvedValue({ authenticated: false, user: null, csrfToken: null })
    const { router } = await import('../index')
    await router.push('/uploads')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.returnUrl).toBe('/uploads')
  })

  it('keeps members out of admin-only pages', async () => {
    session.mockResolvedValue({
      authenticated: true,
      csrfToken: 't',
      user: { gitHubId: 2, login: 'm', name: null, avatarUrl: null, role: 'Member' },
    })
    const { router } = await import('../index')
    await router.push('/actions')
    expect(router.currentRoute.value.name).toBe('gallery')
  })
})
