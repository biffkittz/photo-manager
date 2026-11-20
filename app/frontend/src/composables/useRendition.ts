import { onBeforeUnmount, ref, watch, type Ref } from 'vue'
import { notifyUnauthorized } from '@/api/http'
import type { ProcessingState } from '@/api/types'

export type RenditionStatus = 'ready' | 'pending' | 'unavailable'

/**
 * Tracks whether a rendition can be shown. Ready renditions are loaded straight into <img> (cookie-authenticated,
 * cached immutably). When a photo has none yet, the API answers 202 and asks a worker to create one; this
 * composable polls until it is ready, then hands the URL to <img>. Polling stops on unmount or URL change.
 *
 * `state` is the photo's rendition state, or undefined while the photo itself is still loading (nothing is fetched
 * until it is known). Failed and unsupported renditions are reported as unavailable without a request.
 */
export function useRendition(
  url: Ref<string>,
  state: Ref<ProcessingState | undefined>,
  options = { intervalMs: 3000, maxAttempts: 40 },
) {
  const status = ref<RenditionStatus>(initialStatus(state.value))
  let controller: AbortController | null = null
  let timer: ReturnType<typeof setTimeout> | null = null

  function stop(): void {
    controller?.abort()
    controller = null
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
  }

  async function poll(attempt: number): Promise<void> {
    controller = new AbortController()
    try {
      const response = await fetch(url.value, {
        credentials: 'same-origin',
        signal: controller.signal,
      })
      if (response.status === 200) {
        // Read the (small) body so the browser can cache it; the <img> then loads it without a second download.
        await response.blob()
        status.value = 'ready'
        return
      }

      if (response.status === 401) {
        notifyUnauthorized()
        status.value = 'unavailable'
        return
      }

      if (response.status !== 202 || attempt >= options.maxAttempts) {
        status.value = 'unavailable'
        return
      }

      timer = setTimeout(() => void poll(attempt + 1), options.intervalMs)
    } catch (error) {
      if (!(error instanceof DOMException && error.name === 'AbortError')) {
        status.value = 'unavailable'
      }
    }
  }

  watch(
    [url, state],
    ([, current]) => {
      stop()
      status.value = initialStatus(current)
      if (current === 'NotStarted' || current === 'Pending') {
        void poll(1)
      }
    },
    { immediate: true },
  )

  onBeforeUnmount(stop)
  return { status, markUnavailable: () => (status.value = 'unavailable') }
}

function initialStatus(state: ProcessingState | undefined): RenditionStatus {
  switch (state) {
    case 'Ready':
      return 'ready'
    case 'Failed':
    case 'Unsupported':
      return 'unavailable'
    default:
      return 'pending'
  }
}
