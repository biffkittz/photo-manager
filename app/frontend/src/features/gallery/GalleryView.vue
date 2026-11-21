<script setup lang="ts">
/**
 * Gallery container: search, the timeline (a year or month: ?year=&month=), "On this day", sort, density, infinite
 * scroll, multi-select (add to an album, ZIP download) and the nested viewer. Opened as /gallery?addTo=<album> from
 * an album's "Add photos", it is that album's photo picker: the selection goes straight into the album.
 */
import { computed, defineAsyncComponent, provide, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '@/api/http'
import type { GalleryPeriod, GallerySort } from '@/api/types'
import BaseButton from '@/components/BaseButton.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import PageHeader from '@/components/PageHeader.vue'
import SelectionBar from '@/components/SelectionBar.vue'
import { useAddToAlbum, useAlbumQuery } from '@/composables/useAlbums'
import { useDebouncedRef } from '@/composables/useDebouncedRef'
import { useDownload } from '@/composables/useDownload'
import { useInfiniteScroll } from '@/composables/useInfiniteScroll'
import { useGalleryQuery, useOnThisDayQuery, useTimelineQuery } from '@/composables/usePhotos'
import { useSelection } from '@/composables/useSelection'
import { useNotificationsStore } from '@/stores/notifications'
import { usePreferencesStore } from '@/stores/preferences'
import { periodFromQuery, periodQuery } from '@/utils/calendar'
import { formatPeriod, formatPhotoCount } from '@/utils/format'
import { photoNavigationKey } from './galleryNavigation'
import OnThisDay from './OnThisDay.vue'
import PhotoGrid from './PhotoGrid.vue'
import PhotoTile from './PhotoTile.vue'
import SearchBar from './SearchBar.vue'
import TimelineBar from './TimelineBar.vue'

// Loaded when first opened: keeps the form-validation libraries out of the gallery's initial download.
const AddToAlbumDialog = defineAsyncComponent(
  () => import('@/features/albums/AddToAlbumDialog.vue'),
)

const route = useRoute()
const router = useRouter()
const notifications = useNotificationsStore()
const preferences = usePreferencesStore()
const { density, sort } = storeToRefs(preferences)

// The search lives in the URL (?q=) so results are shareable and survive reloads; so do the year and month.
const search = ref(typeof route.query.q === 'string' ? route.query.q : '')
const debouncedSearch = useDebouncedRef(search, 300)
watch(debouncedSearch, (q) => void router.replace({ query: { ...route.query, q: q || undefined } }))
const period = computed(() => periodFromQuery(route.query))
// A year or month is a stretch of time: it is always shown in the order the photos were taken (newest first).
const listSort = computed<GallerySort>(() => (period.value ? 'taken' : sort.value))
const pickingFor = computed(() =>
  typeof route.query.addTo === 'string' && route.query.addTo ? route.query.addTo : null,
)

const { data, error, isPending, isFetchingNextPage, hasNextPage, fetchNextPage, refetch } =
  useGalleryQuery(debouncedSearch, listSort, period)
const photos = computed(() => data.value?.pages.flatMap((p) => p.items) ?? [])
const photoIds = computed(() => photos.value.map((p) => p.id))
const timeline = useTimelineQuery(debouncedSearch)
const onThisDay = useOnThisDayQuery()
const pickingAlbum = useAlbumQuery(pickingFor)

const memories = computed(() => onThisDay.data.value ?? [])
const memoryIds = computed(() => memories.value.flatMap((year) => year.photos.map((p) => p.id)))
const showMemories = computed(
  () => !pickingFor.value && !period.value && !debouncedSearch.value && memories.value.length > 0,
)

/** Photos in the chosen year or month (from the timeline, which follows the search). */
const periodCount = computed(() => {
  const current = period.value
  if (!current || !timeline.data.value) return null
  return timeline.data.value
    .filter((m) => m.year === current.year && (!current.month || m.month === current.month))
    .reduce((sum, m) => sum + m.count, 0)
})

/** The next older year (or month) with photos, offered at the end of the chosen one. */
const olderPeriod = computed<GalleryPeriod | null>(() => {
  const current = period.value
  const months = timeline.data.value ?? []
  if (!current) return null
  const older = months.find((m) =>
    current.month
      ? m.year < current.year || (m.year === current.year && m.month < current.month)
      : m.year < current.year,
  )
  if (!older) return null
  return current.month ? { year: older.year, month: older.month } : { year: older.year }
})

const sentinel = ref<HTMLElement | null>(null)
async function loadMore(): Promise<void> {
  if (hasNextPage.value && !isFetchingNextPage.value) {
    await fetchNextPage()
  }
}
useInfiniteScroll(sentinel, () => void loadMore())

// The viewer steps through the list the photo was opened from: the grid, or "On this day" (also when such a photo
// is opened from a link and is not in the grid).
const openedFromMemories = ref(false)
const viewingMemories = computed(() => {
  const id = route.params.id
  if (typeof id !== 'string' || !memoryIds.value.includes(id)) return false
  return openedFromMemories.value || !photoIds.value.includes(id)
})
provide(photoNavigationKey, {
  ids: computed(() => (viewingMemories.value ? memoryIds.value : photoIds.value)),
  loadMore: () => (viewingMemories.value ? Promise.resolve() : loadMore()),
  photoRoute: (id) => ({ name: 'photo', params: { id }, query: route.query }),
  closeRoute: () => ({ name: 'gallery', query: route.query }),
})

function open(id: string, fromMemories = false): void {
  openedFromMemories.value = fromMemories
  void router.push({ name: 'photo', params: { id }, query: route.query })
}

const selection = useSelection({ ids: photoIds, hasNextPage, fetchNextPage: () => fetchNextPage() })
const { selected } = selection
const { state: downloadState, download } = useDownload()
const addingToAlbum = ref(false)
const addToAlbum = useAddToAlbum()

async function addToPickingAlbum(): Promise<void> {
  const albumId = pickingFor.value
  const chosen = [...selected.value]
  if (!albumId) return
  try {
    const album = await addToAlbum.mutateAsync({ id: albumId, photoIds: chosen })
    selection.clear()
    notifications.notify('success', `Added ${formatPhotoCount(chosen.length)} to “${album.name}”.`)
    await router.push({ name: 'album', params: { albumId } })
  } catch (e) {
    notifications.notify(
      'error',
      e instanceof ApiError ? e.message : 'The photos could not be added.',
    )
  }
}

const sortOptions: { value: GallerySort; label: string }[] = [
  { value: 'uploaded', label: 'Recently uploaded' },
  { value: 'taken', label: 'Date taken' },
]

const title = computed(() => {
  if (!pickingFor.value) return 'Gallery'
  const name = pickingAlbum.data.value?.name
  return name ? `Add photos to “${name}”` : 'Add photos to the album'
})

const emptyTitle = computed(() => {
  if (debouncedSearch.value) return 'No matching photos'
  return period.value ? `No photos from ${formatPeriod(period.value)}` : 'No photos yet'
})
const emptyText = computed(() => {
  if (debouncedSearch.value) return 'Try different words.'
  return period.value
    ? 'Choose another year or month.'
    : 'Upload photos or run a re-index from Actions to import the archive.'
})
</script>

<template>
  <!-- On phones the selection bar is pinned to the bottom of the screen; the padding keeps it off the last row. -->
  <section :class="{ 'max-sm:pb-32': selected.size > 0 }">
    <!-- Picking photos for an album: the title says so (it gets focus on arrival) and how to finish or cancel. -->
    <PageHeader :title="title">
      <template v-if="pickingFor" #details>
        Select photos, then press Add to album.
        <RouterLink
          :to="{ name: 'album', params: { albumId: pickingFor } }"
          class="ml-1 font-medium text-text underline pointer-coarse:inline-block pointer-coarse:py-3"
          >Cancel</RouterLink
        >
      </template>
      <template #actions>
        <template v-if="!period">
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
        </template>
        <BaseButton :aria-pressed="density === 'compact'" @click="preferences.toggleDensity()"
          >Compact grid</BaseButton
        >
      </template>
    </PageHeader>

    <div class="mb-4 flex flex-wrap items-center gap-3">
      <SearchBar v-model="search" />
      <SelectionBar
        v-if="selected.size > 0"
        :count="selected.size"
        :selecting-all="selection.selectingAll.value"
        @select-all="selection.selectAll()"
        @clear="selection.clear()"
      >
        <BaseButton
          v-if="pickingFor"
          variant="primary"
          :loading="addToAlbum.isPending.value"
          @click="addToPickingAlbum"
          >Add to album</BaseButton
        >
        <template v-else>
          <BaseButton variant="primary" @click="addingToAlbum = true">Add to album</BaseButton>
          <BaseButton :loading="downloadState === 'preparing'" @click="download([...selected])">
            Download {{ selected.size === 1 ? 'original' : 'ZIP' }}
          </BaseButton>
        </template>
      </SelectionBar>
    </div>

    <OnThisDay v-if="showMemories" class="mb-6" :years="memories" @open="open($event, true)" />

    <TimelineBar
      v-if="timeline.data.value?.length"
      class="mb-4"
      :months="timeline.data.value"
      :period="period"
    />

    <h2 v-if="period" class="mb-3 text-lg font-semibold">
      {{ formatPeriod(period) }}
      <span v-if="periodCount !== null" class="text-sm font-normal text-muted"
        >· {{ formatPhotoCount(periodCount) }}</span
      >
    </h2>

    <div v-if="isPending" class="flex justify-center py-20">
      <BaseSpinner label="Loading photos" />
    </div>
    <ErrorAlert v-else-if="error" :message="error.message" retry-label="Retry" @retry="refetch()" />
    <EmptyState v-else-if="photos.length === 0" :title="emptyTitle">{{ emptyText }}</EmptyState>
    <template v-else>
      <PhotoGrid :items="photos" :density="density" label="Photos">
        <template #item="{ item }">
          <PhotoTile
            :photo="item"
            :selected="selected.has(item.id)"
            @open="open"
            @toggle="selection.toggle"
          />
        </template>
      </PhotoGrid>
      <div ref="sentinel" class="flex justify-center py-6">
        <BaseSpinner v-if="isFetchingNextPage" label="Loading more photos" />
        <RouterLink
          v-else-if="!hasNextPage && olderPeriod"
          :to="{ query: { ...route.query, ...periodQuery(olderPeriod) } }"
          class="rounded-md border border-border bg-surface px-4 py-2 text-sm font-medium hover:bg-surface-muted pointer-coarse:py-3"
          >Older: {{ formatPeriod(olderPeriod) }} →</RouterLink
        >
      </div>
    </template>

    <AddToAlbumDialog
      v-if="addingToAlbum"
      v-model:open="addingToAlbum"
      :photo-ids="[...selected]"
      @added="selection.clear()"
    />
    <RouterView />
  </section>
</template>
