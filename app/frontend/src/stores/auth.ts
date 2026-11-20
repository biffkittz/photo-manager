import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '@/api/endpoints'
import { ApiError } from '@/api/http'
import type { SessionResponse, UserInfo } from '@/api/types'

/** Broadcast channel the app's tabs use to tell each other that the session has ended. */
export const SESSION_CHANNEL = 'photo-manager-auth'
const SESSION_ENDED = 'session-ended'

/**
 * Session state. The session itself lives in an HttpOnly cookie the SPA cannot read; this store only holds
 * the user's public profile and the anti-CSRF request token, in memory.
 */
export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserInfo | null>(null)
  const csrfToken = ref<string | null>(null)
  const loaded = ref(false)
  /** Whether the sign-in page offers Google (for family members invited from the People page). */
  const googleSignIn = ref(false)
  /** True when the last session check got no answer (offline, server error); the next navigation asks again. */
  const unreachable = ref(false)
  let loading: Promise<void> | null = null
  const sessionEndedListeners: Array<() => void> = []

  // Signing out, or the session expiring, in one tab ends it in every tab of this browser: the others would
  // otherwise keep showing cached photos until their next request.
  const channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(SESSION_CHANNEL)
  channel?.addEventListener('message', (event: MessageEvent) => {
    if (event.data === SESSION_ENDED) {
      endSession(false)
    }
  })

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

  /**
   * Asks the server about the session and gets a fresh anti-CSRF token. No answer (network or server error)
   * changes nothing: a signed-in user is not treated as signed out because the connection blinked, and the
   * session stays unchecked, so the next navigation asks again.
   */
  async function refresh(): Promise<void> {
    let session: SessionResponse
    try {
      session = await api.session()
    } catch {
      unreachable.value = true
      return
    }

    unreachable.value = false
    loaded.value = true
    googleSignIn.value = session.googleSignIn === true
    if (session.authenticated) {
      user.value = session.user
    } else {
      // Ended on the server: expired, the account was removed, or signed out elsewhere.
      endSession(true)
    }

    csrfToken.value = session.csrfToken
  }

  async function logout(): Promise<void> {
    try {
      await api.logout()
    } catch (error) {
      // 401: the server had already ended the session, which is all signing out is for.
      if (!(error instanceof ApiError && error.status === 401)) {
        throw error
      }
    } finally {
      clear()
    }
  }

  /**
   * Forgets the session locally (after logout or when the server reports 401), in this tab and the others, and
   * tells listeners, which drop everything cached for the previous user (photo data, queued uploads).
   */
  function clear(): void {
    endSession(true)
  }

  function endSession(tellOtherTabs: boolean): void {
    const hadSession = user.value !== null
    user.value = null
    csrfToken.value = null
    if (hadSession) {
      if (tellOtherTabs) {
        channel?.postMessage(SESSION_ENDED)
      }

      sessionEndedListeners.forEach((listener) => listener())
    }
  }

  /** Registers cleanup to run whenever a signed-in session ends (here or in another tab). */
  function onSessionEnded(listener: () => void): void {
    sessionEndedListeners.push(listener)
  }

  return {
    user,
    csrfToken,
    loaded,
    googleSignIn,
    unreachable,
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
