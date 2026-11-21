import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { SELECTION_LIMIT, useSelection } from '../useSelection'
import { useNotificationsStore } from '@/stores/notifications'

/** A paged list of `total` photos, 200 to a page. */
function pagedList(total: number, failOnPage?: number) {
  const ids = ref<string[]>([])
  const hasNextPage = ref(true)
  let page = 0
  const fetchNextPage = vi.fn<() => Promise<{ isError: boolean }>>(() => {
    page++
    if (page === failOnPage) return Promise.resolve({ isError: true })
    ids.value = Array.from({ length: Math.min(total, page * 200) }, (_, i) => `p${i}`)
    hasNextPage.value = ids.value.length < total
    return Promise.resolve({ isError: false })
  })
  return { ids, hasNextPage, fetchNextPage }
}

describe('useSelection', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('toggles and clears', () => {
    const { selected, toggle, clear } = useSelection(pagedList(3))
    toggle('a')
    toggle('b')
    toggle('a')
    expect([...selected.value]).toEqual(['b'])
    clear()
    expect(selected.value.size).toBe(0)
  })

  it('"Select all" loads the rest of the list first', async () => {
    const list = pagedList(450)
    const { selected, selectAll } = useSelection(list)
    await selectAll()
    expect(list.fetchNextPage).toHaveBeenCalledTimes(3)
    expect(selected.value.size).toBe(450)
    expect(useNotificationsStore().items).toEqual([])
  })

  it('stops at the most one download or album change can take, and says so', async () => {
    const list = pagedList(5000)
    const { selected, selectAll } = useSelection(list)
    await selectAll()
    expect(list.fetchNextPage).toHaveBeenCalledTimes(3) // 600 loaded: enough to know there are more
    expect(selected.value.size).toBe(SELECTION_LIMIT)
    expect(useNotificationsStore().items[0]?.message).toContain(`first ${SELECTION_LIMIT} photos`)
  })

  it('selects what it has when a page fails to load, instead of trying forever', async () => {
    const list = pagedList(1000, 2)
    const { selected, selectAll, selectingAll } = useSelection(list)
    await selectAll()
    expect(list.fetchNextPage).toHaveBeenCalledTimes(2)
    expect(selected.value.size).toBe(200)
    expect(selectingAll.value).toBe(false)
  })
})
