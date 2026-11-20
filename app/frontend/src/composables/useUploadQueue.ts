import { computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { api } from '@/api/endpoints'
import { photoKeys } from '@/composables/usePhotos'
import { useUploadsStore } from '@/stores/uploads'

/**
 * Upload queue for components: the store manages the transfers; this polls server-side ingest status for the
 * current batch while anything is still being processed by the workers.
 */
export function useUploadQueue() {
  const store = useUploadsStore()
  const { items, summary, hasProcessing, batchId } = storeToRefs(store)
  const queryClient = useQueryClient()

  const { data } = useQuery({
    queryKey: computed(() => ['uploads', batchId.value]),
    queryFn: ({ signal }) => api.uploads(batchId.value, signal),
    enabled: hasProcessing,
    refetchInterval: 2000,
  })

  watch(data, (statuses) => {
    if (statuses) {
      store.applyServerStatus(statuses)
    }
  })

  // New photos should be in the gallery as soon as their uploads finish, not after the cache goes stale.
  watch(
    () => summary.value.done,
    (done, previous) => {
      if (done > previous) {
        void queryClient.invalidateQueries({ queryKey: photoKeys.all })
      }
    },
  )

  return {
    items,
    summary,
    enqueue: store.enqueue,
    cancel: store.cancel,
    retry: store.retry,
    clearFinished: store.clearFinished,
  }
}
