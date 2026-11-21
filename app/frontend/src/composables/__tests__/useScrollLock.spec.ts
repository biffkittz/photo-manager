import { describe, expect, it } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useScrollLock } from '../useScrollLock'

describe('useScrollLock', () => {
  it('stops the page scrolling while any holder is active, and restores it after the last one', async () => {
    const root = document.documentElement
    root.style.overflow = 'auto'
    const first = ref(false)
    const second = ref(true)
    const scope = effectScope()
    scope.run(() => {
      useScrollLock(first)
      useScrollLock(second)
    })
    expect(root.style.overflow).toBe('hidden')

    first.value = true
    second.value = false
    await nextTick()
    expect(root.style.overflow).toBe('hidden')

    first.value = false
    await nextTick()
    expect(root.style.overflow).toBe('auto')

    // Stopping while active (a dialog unmounted while open) releases its lock too.
    first.value = true
    await nextTick()
    expect(root.style.overflow).toBe('hidden')
    scope.stop()
    expect(root.style.overflow).toBe('auto')
  })
})
