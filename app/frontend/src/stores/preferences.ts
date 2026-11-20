import { ref } from 'vue'
import { defineStore } from 'pinia'
import type { GallerySort } from '@/api/types'

export type GridDensity = 'comfortable' | 'compact'

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
  { persist: { key: 'photo-manager.preferences', pick: ['density', 'sort'] } },
)
