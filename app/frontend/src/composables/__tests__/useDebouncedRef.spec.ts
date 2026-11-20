import { afterEach, describe, expect, it, vi } from 'vitest'
import { nextTick, ref } from 'vue'
import { useDebouncedRef } from '../useDebouncedRef'

describe('useDebouncedRef', () => {
  afterEach(() => vi.useRealTimers())

  it('only publishes the value after it stops changing', async () => {
    vi.useFakeTimers()
    const source = ref('a')
    const debounced = useDebouncedRef(source, 300)

    source.value = 'ab'
    await nextTick()
    vi.advanceTimersByTime(200)
    source.value = 'abc'
    await nextTick()
    vi.advanceTimersByTime(200)
    expect(debounced.value).toBe('a')

    vi.advanceTimersByTime(100)
    expect(debounced.value).toBe('abc')
  })
})
