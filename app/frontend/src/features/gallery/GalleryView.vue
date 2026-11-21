<script setup lang="ts">
/** Gallery container: search, sort, density, infinite scroll, multi-select ZIP download and the nested viewer. */
import { computed, provide, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute, useRouter } from 'vue-router'
import type { GallerySort } from '@/api/types'
import BaseButton from '@/components/BaseButton.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useDebouncedRef } from '@/composables/useDebouncedRef'
import { useDownload } from '@/composables/useDownload'
import { useInfiniteScroll } from '@/composables/useInfiniteScroll'
import { useGalleryQuery } from '@/composables/usePhotos'
import { usePreferencesStore } from '@/stores/preferences'
import { galleryNavigationKey } from './galleryNavigation'
import PhotoGrid from './PhotoGrid.vue'
import PhotoTile from './PhotoTile.vue'
import SearchBar from './SearchBar.vue'

const route = useRoute()
const router = useRouter()
const preferences = usePreferencesStore()
const { density, sort } = storeToRefs(preferences)

// The search lives in the URL (?q=) so results are shareable and survive reloads.
const search = ref(typeof route.query.q === 'string' ? route.query.q : '')
const debouncedSearch = useDebouncedRef(search, 300)
watch(debouncedSearch, (q) => void router.replace({ query: { ...route.query, q: q || undefined } }))

const { data, error, isPending, isFetchingNextPage, hasNextPage, fetchNextPage, refetch } =
  useGalleryQuery(debouncedSearch, sort)
const photos = computed(() => data.value?.pages.flatMap((p) => p.items) ?? [])

const sentinel = ref<HTMLElement | null>(null)
async function loadMore(): Promise<void> {
  if (hasNextPage.value && !isFetchingNextPage.value) {
    await fetchNextPage()
  }
}
useInfiniteScroll(sentinel, () => void loadMore())

provide(galleryNavigationKey, { ids: computed(() => photos.value.map((p) => p.id)), loadMore })

const selected = ref(new Set<string>())
function toggle(id: string): void {
  const next = new Set(selected.value)
  if (!next.delete(id)) next.add(id)
  selected.value = next
}

const { state: downloadState, download } = useDownload()
const sortOptions: { value: GallerySort; label: string }[] = [
  { value: 'uploaded', label: 'Recently uploaded' },
  { value: 'taken', label: 'Date taken' },
]

function open(id: string): void {
  void router.push({ name: 'photo', params: { id }, query: route.query })
}
</script>

<template>
  <!-- On phones the selection bar is pinned to the bottom of the screen; the padding keeps it off the last row. -->
  <section :class="{ 'max-sm:pb-20': selected.size > 0 }">
    <PageHeader title="Gallery">
      <template #actions>
        <label class="sr-only" for="gallery-sort">Sort photos</label>
        <select
          id="gallery-sort"
          v-model="sort"
          class="rounded-md border border-border bg-surface px-2 py-2 text-sm pointer-coarse:min-h-11 pointer-coarse:text-base"
        >
          <option v-for="option in sortOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </option>
        </select>
        <BaseButton :aria-pressed="density === 'compact'" @click="preferences.toggleDensity()"
          >Compact grid</BaseButton
        >
      </template>
    </PageHeader>

    <div class="mb-4 flex flex-wrap items-center gap-3">
      <SearchBar v-model="search" />
      <div
        v-if="selected.size > 0"
        class="flex items-center gap-2 max-sm:fixed max-sm:inset-x-0 max-sm:bottom-0 max-sm:z-30 max-sm:border-t max-sm:border-border max-sm:bg-surface max-sm:px-4 max-sm:py-3 max-sm:shadow-lg"
        role="region"
        aria-label="Selection"
      >
        <span class="text-sm max-sm:mr-auto">{{ selected.size }} selected</span>
        <BaseButton
          variant="primary"
          :loading="downloadState === 'preparing'"
          @click="download([...selected])"
        >
          Download {{ selected.size === 1 ? 'original' : 'ZIP' }}
        </BaseButton>
        <BaseButton variant="ghost" @click="selected = new Set()">Clear</BaseButton>
      </div>
    </div>

    <div v-if="isPending" class="flex justify-center py-20">
      <BaseSpinner label="Loading photos" />
    </div>
    <ErrorAlert v-else-if="error" :message="error.message" retry-label="Retry" @retry="refetch()" />
    <EmptyState
      v-else-if="photos.length === 0"
      :title="debouncedSearch ? 'No matching photos' : 'No photos yet'"
    >
      {{
        debouncedSearch
          ? 'Try different words.'
          : 'Upload photos or run a re-index from Actions to import the archive.'
      }}
    </EmptyState>
    <template v-else>
      <PhotoGrid :items="photos" :density="density" label="Photos">
        <template #item="{ item }">
          <PhotoTile
            :photo="item"
            :selected="selected.has(item.id)"
            @open="open"
            @toggle="toggle"
          />
        </template>
      </PhotoGrid>
      <div ref="sentinel" class="flex justify-center py-6">
        <BaseSpinner v-if="isFetchingNextPage" label="Loading more photos" />
      </div>
    </template>

    <RouterView />
  </section>
</template>
