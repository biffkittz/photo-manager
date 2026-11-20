import { ref } from 'vue'
import { defineStore } from 'pinia'
import type { GallerySort } from '@/api/types'

export type GridDensity = 'comfortable' | 'compact'

const allowed: Record<string, readonly unknown[]> = {
  density: ['comfortable', 'compact'] satisfies GridDensity[],
  sort: ['uploaded', 'taken'] satisfies GallerySort[],
}

/** Non-sensitive UI preferences, persisted to localStorage by the persist plugin. */
export const usePreferencesStore = defineStore(
  'preferences',
  () => {
    const density = ref<GridDensity>('comfortable')
    const sort = ref<GallerySort>('uploaded')

    function toggleDensity(): void {
      density.value = density.value === 'comfortable' ? 'compact' : 'comfortable'
    }

    return { density, sort, toggleDensity }
  },
  {
    persist: {
      key: 'photo-manager.preferences',
      pick: ['density', 'sort'],
      validate: (key, value) => allowed[key]?.includes(value) ?? false,
    },
  },
)
