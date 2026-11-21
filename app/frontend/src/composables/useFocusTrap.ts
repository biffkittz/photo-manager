import { nextTick, onBeforeUnmount, watch, type Ref } from 'vue'

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

// Active traps, innermost last. Only the innermost one handles Tab, so a dialog opened from another dialog (adding
// the photo in the viewer to an album) keeps focus to itself.
const activeTraps: object[] = []

function release(trap: object): void {
  const index = activeTraps.indexOf(trap)
  if (index >= 0) activeTraps.splice(index, 1)
}

/**
 * Keeps keyboard focus inside `container` while `active` (WAI-ARIA modal dialog pattern) and restores focus to
 * the previously focused element when it deactivates. Escape calls `onEscape` wherever focus is, even when it was
 * lost (a focused button that became disabled leaves focus on the page body).
 */
export function useFocusTrap(
  container: Ref<HTMLElement | null>,
  active: Ref<boolean>,
  onEscape?: () => void,
) {
  const trap = {}
  let previouslyFocused: HTMLElement | null = null

  function onKeydown(event: KeyboardEvent): void {
    if (!container.value || activeTraps[activeTraps.length - 1] !== trap) {
      return
    }

    if (event.key === 'Escape' && onEscape && !event.defaultPrevented) {
      event.preventDefault()
      onEscape()
      return
    }

    if (event.key !== 'Tab') {
      return
    }

    const focusable = Array.from(container.value.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
      (el) => el.offsetParent !== null || el === document.activeElement,
    )
    if (focusable.length === 0) {
      event.preventDefault()
      return
    }

    const first = focusable[0]!
    const last = focusable[focusable.length - 1]!
    const active = document.activeElement
    const outside =
      !(active instanceof Node) || !container.value.contains(active) || active === container.value
    if (event.shiftKey && (active === first || outside)) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && (active === last || outside)) {
      event.preventDefault()
      first.focus()
    }
  }

  watch(
    active,
    async (isActive) => {
      if (isActive) {
        previouslyFocused = document.activeElement as HTMLElement | null
        activeTraps.push(trap)
        document.addEventListener('keydown', onKeydown)
        await nextTick()
        const target =
          container.value?.querySelector<HTMLElement>('[autofocus], ' + FOCUSABLE) ??
          container.value
        target?.focus()
      } else {
        release(trap)
        document.removeEventListener('keydown', onKeydown)
        previouslyFocused?.focus()
        previouslyFocused = null
      }
    },
    { immediate: true },
  )

  onBeforeUnmount(() => {
    release(trap)
    document.removeEventListener('keydown', onKeydown)
    previouslyFocused?.focus()
  })
}
