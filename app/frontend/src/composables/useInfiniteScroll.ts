import { onBeforeUnmount, watch, type Ref } from 'vue'

/**
 * Calls `onReachEnd` when the sentinel element scrolls into view (IntersectionObserver, no scroll listeners).
 * The observer is disconnected when the sentinel changes or the component unmounts.
 */
export function useInfiniteScroll(
  sentinel: Ref<HTMLElement | null>,
  onReachEnd: () => void,
  rootMargin = '600px',
) {
  let observer: IntersectionObserver | null = null

  const stop = watch(
    sentinel,
    (element) => {
      observer?.disconnect()
      if (!element) {
        return
      }

      observer = new IntersectionObserver(
        (entries) => {
          if (entries.some((e) => e.isIntersecting)) {
            onReachEnd()
          }
        },
        { rootMargin },
      )
      observer.observe(element)
    },
    { immediate: true },
  )

  onBeforeUnmount(() => {
    stop()
    observer?.disconnect()
  })
}
