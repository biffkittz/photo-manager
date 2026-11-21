<script setup lang="ts">
/**
 * Puts photos in an album: a new one (named here) or one picked from the list, most recently changed first. Used for
 * the gallery's selection and for the photo in the viewer.
 */
import { computed, useId } from 'vue'
import { ApiError } from '@/api/http'
import type { AlbumSummary } from '@/api/types'
import BaseSpinner from '@/components/BaseSpinner.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import ModalDialog from '@/components/ModalDialog.vue'
import PhotoThumbnail from '@/components/PhotoThumbnail.vue'
import { useAddToAlbum, useAlbumsQuery, useCreateAlbum } from '@/composables/useAlbums'
import { useNotificationsStore } from '@/stores/notifications'
import { formatPhotoCount } from '@/utils/format'
import AlbumNameForm from './AlbumNameForm.vue'

const props = defineProps<{ photoIds: string[] }>()
const emit = defineEmits<{ added: [album: AlbumSummary] }>()
const open = defineModel<boolean>('open', { required: true })

const notifications = useNotificationsStore()
const listHeadingId = useId()
const albums = useAlbumsQuery()
const createAlbum = useCreateAlbum()
const addToAlbum = useAddToAlbum()
const busy = computed(() => createAlbum.isPending.value || addToAlbum.isPending.value)
const what = computed(() =>
  props.photoIds.length === 1 ? 'the photo' : formatPhotoCount(props.photoIds.length),
)

function done(album: AlbumSummary, message: string): void {
  notifications.notify('success', message)
  open.value = false
  emit('added', album)
}

async function create(name: string): Promise<void> {
  try {
    const album = await createAlbum.mutateAsync({ name, photoIds: props.photoIds })
    done(album, `Created “${album.name}” with ${what.value}.`)
  } catch (error) {
    notifications.notify(
      'error',
      error instanceof ApiError ? error.message : 'The album could not be created.',
    )
  }
}

async function add(album: AlbumSummary): Promise<void> {
  if (busy.value) return
  try {
    done(
      await addToAlbum.mutateAsync({ id: album.id, photoIds: props.photoIds }),
      `Added ${what.value} to “${album.name}”.`,
    )
  } catch (error) {
    notifications.notify(
      'error',
      error instanceof ApiError ? error.message : 'The photos could not be added.',
    )
  }
}
</script>

<template>
  <ModalDialog
    v-model:open="open"
    :title="
      photoIds.length === 1
        ? 'Add to album'
        : `Add ${formatPhotoCount(photoIds.length)} to an album`
    "
  >
    <div class="flex flex-col gap-6">
      <AlbumNameForm
        label="New album"
        submit-label="Create album"
        :saving="createAlbum.isPending.value"
        @save="create"
      />

      <BaseSpinner v-if="albums.isPending.value" label="Loading albums" />
      <ErrorAlert
        v-else-if="albums.error.value"
        :message="albums.error.value.message"
        retry-label="Retry"
        @retry="albums.refetch()"
      />
      <section v-else-if="albums.data.value?.length" :aria-labelledby="listHeadingId">
        <h3 :id="listHeadingId" class="mb-2 text-sm font-medium">Or add to an album</h3>
        <ul class="flex flex-col gap-1">
          <li v-for="album in albums.data.value" :key="album.id">
            <button
              type="button"
              class="flex w-full items-center gap-3 rounded-md p-2 text-left hover:bg-surface-muted disabled:opacity-60"
              :disabled="busy"
              @click="add(album)"
            >
              <span class="size-12 shrink-0 overflow-hidden rounded bg-surface-muted">
                <PhotoThumbnail v-if="album.cover" :photo="album.cover" />
              </span>
              <span class="min-w-0">
                <span class="block truncate font-medium">{{ album.name }}</span>
                <span class="block text-xs text-muted">{{
                  formatPhotoCount(album.photoCount)
                }}</span>
              </span>
            </button>
          </li>
        </ul>
      </section>
    </div>
  </ModalDialog>
</template>
