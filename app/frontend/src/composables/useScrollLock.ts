import { onScopeDispose, watch, type Ref } from 'vue'

let locks = 0
let saved = { overflow: '', paddingRight: '' }

/**
 * Stops the page from scrolling while `active` (an open dialog): on touch screens a swipe that reaches the end of
 * the dialog would otherwise scroll the gallery underneath and load more photos. The scroll position is kept, and
 * the width of a vanishing desktop scrollbar is padded so the page behind does not shift. Several holders may
 * lock at once; the page scrolls again when the last one releases.
 */
export function useScrollLock(active: Ref<boolean>): void {
  let held = false

  function hold(value: boolean): void {
    if (value === held) return
    held = value
    const root = document.documentElement
    if (value) {
      if (locks++ === 0) {
        saved = { overflow: root.style.overflow, paddingRight: root.style.paddingRight }
        const scrollbar = root.clientWidth > 0 ? window.innerWidth - root.clientWidth : 0
        root.style.overflow = 'hidden'
        if (scrollbar > 0) root.style.paddingRight = `${scrollbar}px`
      }
    } else if (--locks === 0) {
      root.style.overflow = saved.overflow
      root.style.paddingRight = saved.paddingRight
    }
  }

  watch(active, hold, { immediate: true })
  onScopeDispose(() => hold(false))
}
