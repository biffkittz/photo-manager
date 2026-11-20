import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '@/api/endpoints'
import type { UserInfo } from '@/api/types'

/**
 * Session state. The session itself lives in an HttpOnly cookie the SPA cannot read; this store only holds
 * the user's public profile and the anti-CSRF request token, in memory.
 */
export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserInfo | null>(null)
  const csrfToken = ref<string | null>(null)
  const loaded = ref(false)
  let loading: Promise<void> | null = null
  const sessionEndedListeners: Array<() => void> = []

  const isAuthenticated = computed(() => user.value !== null)
  const isAdmin = computed(() => user.value?.role === 'Admin')
  const displayName = computed(() => user.value?.name || user.value?.login || '')

  /** Loads the session once; concurrent callers share the same request. */
  function ensureLoaded(): Promise<void> {
    if (loaded.value) {
      return Promise.resolve()
    }

    loading ??= refresh().finally(() => {
      loading = null
    })
    return loading
  }

  async function refresh(): Promise<void> {
    try {
      const session = await api.session()
      user.value = session.authenticated ? session.user : null
      csrfToken.value = session.csrfToken
    } catch {
      user.value = null
      csrfToken.value = null
    } finally {
      loaded.value = true
    }
  }

  async function logout(): Promise<void> {
    try {
      await api.logout()
    } finally {
      clear()
    }
  }

  /**
   * Forgets the session locally (after logout or when the server reports 401) and tells listeners, which drop
   * everything cached for the previous user (photo data, queued uploads).
   */
  function clear(): void {
    const hadSession = user.value !== null
    user.value = null
    csrfToken.value = null
    if (hadSession) {
      sessionEndedListeners.forEach((listener) => listener())
    }
  }

  /** Registers cleanup to run whenever a signed-in session ends. */
  function onSessionEnded(listener: () => void): void {
    sessionEndedListeners.push(listener)
  }

  return {
    user,
    csrfToken,
    loaded,
    isAuthenticated,
    isAdmin,
    displayName,
    ensureLoaded,
    refresh,
    logout,
    clear,
    onSessionEnded,
  }
})
