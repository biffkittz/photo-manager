<script setup lang="ts">
/**
 * One album: its photos (oldest first, so it reads like the event), and everything that can be done with it. Anyone
 * signed in can add or remove photos, rename it, download it, choose its cover (in the photo viewer) or delete it;
 * deleting and removing ask first, and never touch the photos themselves.
 */
import { computed, provide, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ApiError } from '@/api/http'
import BaseButton from '@/components/BaseButton.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import EmptyState from '@/components/EmptyState.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import ModalDialog from '@/components/ModalDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import SelectionBar from '@/components/SelectionBar.vue'
import {
  useAlbumPhotosQuery,
  useAlbumQuery,
  useDeleteAlbum,
  useRemoveFromAlbum,
  useUpdateAlbum,
} from '@/composables/useAlbums'
import { useDownload } from '@/composables/useDownload'
import { useInfiniteScroll } from '@/composables/useInfiniteScroll'
import { useSelection } from '@/composables/useSelection'
import { photoNavigationKey } from '@/features/gallery/galleryNavigation'
import PhotoGrid from '@/features/gallery/PhotoGrid.vue'
import PhotoTile from '@/features/gallery/PhotoTile.vue'
import { usePreferencesStore } from '@/stores/preferences'
import { useNotificationsStore } from '@/stores/notifications'
import { formatMonthRange, formatPhotoCount } from '@/utils/format'
import AlbumNameForm from './AlbumNameForm.vue'

const props = defineProps<{ albumId: string }>()

const router = useRouter()
const notifications = useNotificationsStore()
const preferences = usePreferencesStore()
const albumQuery = useAlbumQuery(() => props.albumId)
const album = computed(() => albumQuery.data.value)
const photosQuery = useAlbumPhotosQuery(() => props.albumId)
const photos = computed(() => photosQuery.data.value?.pages.flatMap((p) => p.items) ?? [])
const ids = computed(() => photos.value.map((p) => p.id))
const missing = computed(
  () => albumQuery.error.value instanceof ApiError && albumQuery.error.value.status === 404,
)

const sentinel = ref<HTMLElement | null>(null)
async function loadMore(): Promise<void> {
  if (photosQuery.hasNextPage.value && !photosQuery.isFetchingNextPage.value) {
    await photosQuery.fetchNextPage()
  }
}
useInfiniteScroll(sentinel, () => void loadMore())

provide(photoNavigationKey, {
  ids,
  loadMore,
  photoRoute: (id) => ({ name: 'album-photo', params: { albumId: props.albumId, id } }),
  closeRoute: () => ({ name: 'album', params: { albumId: props.albumId } }),
  album,
})

const selection = useSelection({
  ids,
  hasNextPage: photosQuery.hasNextPage,
  fetchNextPage: () => photosQuery.fetchNextPage(),
})
const { selected } = selection
const { state: downloadState, download } = useDownload()
const updateAlbum = useUpdateAlbum()
const removeFromAlbum = useRemoveFromAlbum()
const deleteAlbum = useDeleteAlbum()
const renaming = ref(false)
const deleting = ref(false)
const removing = ref(false)

const details = computed(() =>
  album.value
    ? [
        formatPhotoCount(album.value.photoCount),
        formatMonthRange(album.value.firstDate, album.value.lastDate),
      ]
        .filter(Boolean)
        .join(' · ')
    : '',
)

function failure(error: unknown, fallback: string): void {
  notifications.notify('error', error instanceof ApiError ? error.message : fallback)
}

function open(id: string): void {
  void router.push({ name: 'album-photo', params: { albumId: props.albumId, id } })
}

function addPhotos(): void {
  void router.push({ name: 'gallery', query: { addTo: props.albumId } })
}

async function rename(name: string): Promise<void> {
  try {
    await updateAlbum.mutateAsync({ id: props.albumId, name })
    renaming.value = false
    notifications.notify('success', 'Album renamed.')
  } catch (error) {
    failure(error, 'The album could not be renamed.')
  }
}

async function remove(): Promise<void> {
  const photoIds = [...selected.value]
  try {
    await removeFromAlbum.mutateAsync({ id: props.albumId, photoIds })
    selection.clear()
    notifications.notify(
      'success',
      `Removed ${formatPhotoCount(photoIds.length)} from the album. They are still in the gallery.`,
    )
  } catch (error) {
    failure(error, 'The photos could not be removed.')
  } finally {
    removing.value = false
  }
}

async function destroy(): Promise<void> {
  const name = album.value?.name ?? 'The album'
  try {
    await deleteAlbum.mutateAsync(props.albumId)
    deleting.value = false
    notifications.notify('success', `“${name}” was deleted. Its photos are still in the gallery.`)
    await router.push({ name: 'albums' })
  } catch (error) {
    deleting.value = false
    failure(error, 'The album could not be deleted.')
  }
}
</script>

<template>
  <section :class="{ 'max-sm:pb-32': selected.size > 0 }">
    <EmptyState v-if="missing" title="Album not found">
      It may have been deleted. Its photos are still in the gallery.
      <template #action>
        <RouterLink :to="{ name: 'albums' }" class="font-medium underline">All albums</RouterLink>
      </template>
    </EmptyState>
    <ErrorAlert
      v-else-if="albumQuery.error.value"
      :message="albumQuery.error.value.message"
      retry-label="Retry"
      @retry="albumQuery.refetch()"
    />
    <div v-else-if="!album" class="flex justify-center py-20">
      <BaseSpinner label="Loading album" />
    </div>
    <template v-else>
      <PageHeader
        :title="album.name"
        :breadcrumb="[{ label: 'Albums', to: { name: 'albums' } }, { label: album.name }]"
      >
        <template #details>{{ details }}</template>
        <template #actions>
          <div class="grid grid-cols-2 gap-2 sm:flex">
            <BaseButton variant="primary" @click="addPhotos">Add photos</BaseButton>
            <BaseButton
              :disabled="album.photoCount === 0"
              :loading="downloadState === 'preparing'"
              @click="download({ albumId })"
              >Download</BaseButton
            >
            <BaseButton @click="renaming = true">Rename</BaseButton>
            <BaseButton @click="deleting = true">Delete</BaseButton>
          </div>
        </template>
      </PageHeader>

      <SelectionBar
        v-if="selected.size > 0"
        class="sm:mb-4"
        :count="selected.size"
        :selecting-all="selection.selectingAll.value"
        @select-all="selection.selectAll()"
        @clear="selection.clear()"
      >
        <BaseButton
          variant="primary"
          :loading="downloadState === 'preparing'"
          @click="download([...selected])"
          >Download {{ selected.size === 1 ? 'original' : 'ZIP' }}</BaseButton
        >
        <BaseButton @click="removing = true">Remove from album</BaseButton>
      </SelectionBar>

      <div v-if="photosQuery.isPending.value" class="flex justify-center py-20">
        <BaseSpinner label="Loading photos" />
      </div>
      <ErrorAlert
        v-else-if="photosQuery.error.value"
        :message="photosQuery.error.value.message"
        retry-label="Retry"
        @retry="photosQuery.refetch()"
      />
      <EmptyState v-else-if="photos.length === 0" title="This album is empty">
        Choose photos for it in the gallery.
        <template #action>
          <BaseButton variant="primary" @click="addPhotos">Add photos</BaseButton>
        </template>
      </EmptyState>
      <template v-else>
        <PhotoGrid :items="photos" :density="preferences.density" label="Photos in this album">
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
          <BaseSpinner v-if="photosQuery.isFetchingNextPage.value" label="Loading more photos" />
        </div>
      </template>

      <ModalDialog v-model:open="renaming" title="Rename album">
        <AlbumNameForm
          :initial="album.name"
          label="Name"
          submit-label="Save"
          cancelable
          :saving="updateAlbum.isPending.value"
          @save="rename"
          @cancel="renaming = false"
        />
      </ModalDialog>

      <ModalDialog v-model:open="removing" title="Remove from album">
        <p class="text-sm">
          Remove {{ formatPhotoCount(selected.size) }} from “{{ album.name }}”? They stay in the
          gallery.
        </p>
        <template #footer>
          <BaseButton variant="ghost" @click="removing = false">Keep</BaseButton>
          <BaseButton variant="danger" :loading="removeFromAlbum.isPending.value" @click="remove"
            >Remove</BaseButton
          >
        </template>
      </ModalDialog>

      <ModalDialog v-model:open="deleting" title="Delete album">
        <p class="text-sm">
          Delete “{{ album.name }}” for everyone? Its photos stay in the gallery.
        </p>
        <template #footer>
          <BaseButton variant="ghost" @click="deleting = false">Keep</BaseButton>
          <BaseButton variant="danger" :loading="deleteAlbum.isPending.value" @click="destroy"
            >Delete album</BaseButton
          >
        </template>
      </ModalDialog>
    </template>

    <RouterView />
  </section>
</template>
