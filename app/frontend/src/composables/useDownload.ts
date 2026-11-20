import { onBeforeUnmount, ref } from 'vue'
import { api, urls } from '@/api/endpoints'
import { ApiError } from '@/api/http'
import { useNotificationsStore } from '@/stores/notifications'

export type DownloadState = 'idle' | 'preparing' | 'failed'

/**
 * Downloads originals. The web tier cannot decrypt originals (split keys), so a worker prepares the file first;
 * this polls until it is ready, then triggers a normal browser download (cookie-authenticated link).
 */
export function useDownload(pollIntervalMs = 1500, timeoutMs = 10 * 60 * 1000) {
  const state = ref<DownloadState>('idle')
  const notifications = useNotificationsStore()

  // Aborted when the component goes away (e.g. the viewer is closed): no further polling, no late download,
  // no error toast for a download nobody is waiting for any more.
  const lifetime = new AbortController()
  onBeforeUnmount(() => lifetime.abort())

  async function download(photoIds: string[]): Promise<void> {
    if (photoIds.length === 0 || state.value === 'preparing') {
      return
    }

    state.value = 'preparing'
    try {
      let current = await api.createDownload(photoIds)
      const deadline = Date.now() + timeoutMs
      while (current.status === 'Pending' && Date.now() < deadline) {
        await new Promise((resolve) => setTimeout(resolve, pollIntervalMs))
        if (lifetime.signal.aborted) {
          return
        }

        current = await api.download(current.id, lifetime.signal)
      }

      if (lifetime.signal.aborted) {
        return
      }

      if (current.status !== 'Ready') {
        throw new Error(current.error ?? 'The download could not be prepared in time.')
      }

      if (current.skippedCount > 0) {
        notifications.notify(
          'info',
          current.skippedCount === 1
            ? '1 photo was left out because it is no longer available.'
            : `${current.skippedCount} photos were left out because they are no longer available.`,
        )
      }

      const link = document.createElement('a')
      link.href = urls.downloadContent(current.id)
      link.download = current.fileName
      link.rel = 'noopener'
      document.body.append(link)
      link.click()
      link.remove()
      state.value = 'idle'
    } catch (error) {
      if (lifetime.signal.aborted) {
        return
      }

      state.value = 'failed'
      notifications.notify(
        'error',
        error instanceof ApiError || error instanceof Error ? error.message : 'Download failed.',
      )
    }
  }

  return { state, download }
}
