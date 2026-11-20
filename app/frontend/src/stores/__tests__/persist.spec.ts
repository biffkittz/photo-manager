import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { persistPlugin } from '../plugins/persist'
import { usePreferencesStore } from '../preferences'

const key = 'photo-manager.preferences'

/** In-memory Storage (the test environment has no usable localStorage of its own). */
class MemoryStorage implements Storage {
  private readonly items = new Map<string, string>()

  get length(): number {
    return this.items.size
  }

  clear(): void {
    this.items.clear()
  }

  getItem(name: string): string | null {
    return this.items.get(name) ?? null
  }

  key(index: number): string | null {
    return [...this.items.keys()][index] ?? null
  }

  removeItem(name: string): void {
    this.items.delete(name)
  }

  setItem(name: string, value: string): void {
    this.items.set(name, value)
  }
}

let storage: MemoryStorage

function freshPinia(): void {
  const pinia = createPinia()
  pinia.use(persistPlugin)
  createApp({}).use(pinia) // plugins apply once pinia is installed in an app
  setActivePinia(pinia)
}

describe('persist plugin', () => {
  beforeEach(() => {
    storage = new MemoryStorage()
    Object.defineProperty(window, 'localStorage', { get: () => storage, configurable: true })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('restores and saves the listed preferences', async () => {
    storage.setItem(key, JSON.stringify({ density: 'compact', sort: 'taken' }))
    freshPinia()
    const preferences = usePreferencesStore()

    expect(preferences.density).toBe('compact')
    expect(preferences.sort).toBe('taken')

    preferences.toggleDensity()
    await vi.waitFor(() =>
      expect(JSON.parse(storage.getItem(key)!)).toEqual({
        density: 'comfortable',
        sort: 'taken',
      }),
    )
  })

  it('ignores stored values of the wrong kind, unknown values and inherited keys', () => {
    storage.setItem(
      key,
      '{"density": 42, "sort": "<img src=x>", "__proto__": {"density": "compact"}}',
    )
    freshPinia()
    const preferences = usePreferencesStore()

    expect(preferences.density).toBe('comfortable')
    expect(preferences.sort).toBe('uploaded')
  })

  it('drops unreadable stored data', () => {
    storage.setItem(key, '{not json')
    freshPinia()

    expect(usePreferencesStore().density).toBe('comfortable')
    expect(storage.getItem(key)).toBeNull()
  })

  it('keeps working when storage cannot be used at all', () => {
    vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError')
    })
    freshPinia()
    const preferences = usePreferencesStore()

    expect(() => preferences.toggleDensity()).not.toThrow()
    expect(preferences.density).toBe('compact')
  })

  it('keeps working when saving fails', async () => {
    const setItem = vi.spyOn(MemoryStorage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota exceeded', 'QuotaExceededError')
    })
    freshPinia()
    const preferences = usePreferencesStore()

    preferences.toggleDensity()

    await vi.waitFor(() => expect(setItem).toHaveBeenCalled())
    expect(preferences.density).toBe('compact')
  })
})
