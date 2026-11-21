import { ref, type Ref } from 'vue'
import { useNotificationsStore } from '@/stores/notifications'

/** The most photos one download, or one change to an album, can hold. */
export const SELECTION_LIMIT = 500

/**
 * Multi-select over a paged photo list. "Select all" loads the remaining pages first (up to the limit), so it
 * means every photo the list shows: the whole month, search result or album, not just what has scrolled into view.
 */
export function useSelection(list: {
  ids: Ref<string[]>
  hasNextPage: Ref<boolean>
  fetchNextPage: () => Promise<{ isError: boolean }>
}) {
  const notifications = useNotificationsStore()
  const selected = ref(new Set<string>())
  const selectingAll = ref(false)

  function toggle(id: string): void {
    const next = new Set(selected.value)
    if (!next.delete(id)) next.add(id)
    selected.value = next
  }

  function clear(): void {
    selected.value = new Set()
  }

  async function selectAll(): Promise<void> {
    if (selectingAll.value) return
    selectingAll.value = true
    try {
      while (list.hasNextPage.value && list.ids.value.length <= SELECTION_LIMIT) {
        if ((await list.fetchNextPage()).isError) break
      }

      selected.value = new Set(list.ids.value.slice(0, SELECTION_LIMIT))
      if (list.ids.value.length > SELECTION_LIMIT) {
        notifications.notify(
          'info',
          `Selected the first ${SELECTION_LIMIT} photos, the most you can download or add to an album at once.`,
        )
      }
    } finally {
      selectingAll.value = false
    }
  }

  return { selected, selectingAll, toggle, clear, selectAll }
}
