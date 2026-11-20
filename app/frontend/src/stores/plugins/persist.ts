import type { PiniaPluginContext } from 'pinia'

/** Opt-in persistence: `defineStore(id, setup, { persist: { key, pick } })`. */
export interface PersistOptions {
  /** localStorage key. */
  key: string
  /** State properties to persist. Only non-sensitive UI preferences may be listed. */
  pick: string[]
}

/**
 * Minimal Pinia store plugin that persists whitelisted state to localStorage. Deliberately tiny (no extra
 * dependency) and opt-in per property, so tokens or personal data can never be persisted by accident.
 */
export function persistPlugin({ store, options }: PiniaPluginContext) {
  const config = options.persist
  if (!config || typeof window === 'undefined') {
    return
  }

  try {
    const saved = window.localStorage.getItem(config.key)
    if (saved) {
      const parsed = JSON.parse(saved) as Record<string, unknown>
      store.$patch((state) => {
        for (const key of config.pick.filter((k) => k in parsed)) {
          ;(state as Record<string, unknown>)[key] = parsed[key]
        }
      })
    }
  } catch {
    window.localStorage.removeItem(config.key)
  }

  store.$subscribe((_mutation, state) => {
    const subset = Object.fromEntries(
      config.pick.map((k) => [k, (state as Record<string, unknown>)[k]]),
    )
    window.localStorage.setItem(config.key, JSON.stringify(subset))
  })
}
