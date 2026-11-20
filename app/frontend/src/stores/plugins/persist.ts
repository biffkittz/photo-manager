import type { PiniaPluginContext } from 'pinia'

/** Opt-in persistence: `defineStore(id, setup, { persist: { key, pick, validate } })`. */
export interface PersistOptions {
  /** localStorage key. */
  key: string
  /** State properties to persist. Only non-sensitive UI preferences may be listed. */
  pick: string[]
  /** Optional check of a restored value (for example an allowed set of strings); false keeps the default. */
  validate?: (key: string, value: unknown) => boolean
}

/**
 * Minimal Pinia store plugin that persists whitelisted state to localStorage. Deliberately tiny (no extra
 * dependency) and opt-in per property, so tokens or personal data can never be persisted by accident.
 *
 * Storage is optional: it can be disabled or full (merely touching `localStorage` throws when site data is
 * blocked), and then preferences are simply not remembered. Stored values are untrusted, since anything on this
 * origin, or the person at the keyboard, can change them: only listed keys, only values of the same kind as the
 * default, and only values the store's `validate` accepts are restored.
 */
export function persistPlugin({ store, options }: PiniaPluginContext) {
  const config = options.persist
  if (!config || typeof window === 'undefined') {
    return
  }

  const storage = localStorageOrNull()
  if (!storage) {
    return
  }

  const saved = read(storage, config.key)
  if (saved) {
    store.$patch((state) => {
      const target = state as Record<string, unknown>
      for (const key of config.pick) {
        if (
          Object.prototype.hasOwnProperty.call(saved, key) &&
          sameKind(target[key], saved[key]) &&
          (config.validate?.(key, saved[key]) ?? true)
        ) {
          target[key] = saved[key]
        }
      }
    })
  }

  store.$subscribe((_mutation, state) => {
    const subset = Object.fromEntries(
      config.pick.map((k) => [k, (state as Record<string, unknown>)[k]]),
    )
    try {
      storage.setItem(config.key, JSON.stringify(subset))
    } catch {
      // Quota exceeded or storage blocked meanwhile: the preference is just not remembered.
    }
  })
}

function localStorageOrNull(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

function read(storage: Storage, key: string): Record<string, unknown> | null {
  try {
    const raw = storage.getItem(key)
    const parsed: unknown = raw ? JSON.parse(raw) : null
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    try {
      storage.removeItem(key)
    } catch {
      // Storage became unavailable: nothing to clean up.
    }

    return null
  }
}

function sameKind(current: unknown, restored: unknown): boolean {
  return (
    typeof current === typeof restored &&
    Array.isArray(current) === Array.isArray(restored) &&
    (current === null) === (restored === null)
  )
}
