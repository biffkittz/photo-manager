import { nextTick, onBeforeUnmount, watch, type Ref } from 'vue'

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * Keeps keyboard focus inside `container` while `active` (WAI-ARIA modal dialog pattern) and restores focus to
 * the previously focused element when it deactivates.
 */
export function useFocusTrap(container: Ref<HTMLElement | null>, active: Ref<boolean>) {
  let previouslyFocused: HTMLElement | null = null

  function onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab' || !container.value) {
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
        document.addEventListener('keydown', onKeydown)
        await nextTick()
        const target =
          container.value?.querySelector<HTMLElement>('[autofocus], ' + FOCUSABLE) ??
          container.value
        target?.focus()
      } else {
        document.removeEventListener('keydown', onKeydown)
        previouslyFocused?.focus()
        previouslyFocused = null
      }
    },
    { immediate: true },
  )

  onBeforeUnmount(() => {
    document.removeEventListener('keydown', onKeydown)
    previouslyFocused?.focus()
  })
}
