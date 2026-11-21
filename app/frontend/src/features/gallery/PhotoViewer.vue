<script setup lang="ts">
/**
 * Photo viewer (nested route /gallery/photos/:id or /albums/:albumId/photos/:id, so photos are deep-linkable).
 * Shows the large preview, metadata, description editing, AI re-description, original download and adding to an
 * album (and, in an album, making the photo its cover). Left/right arrow keys, the Previous/Next buttons and, on
 * touch screens, swiping the photo go to the previous/next photo of the list it was opened from.
 *
 * Layout: on phones and tablets the photo spans the width at its own aspect ratio (at most 70% of the screen
 * height) with the details below it, scrolling together, so the two can never overlap. On large screens they sit
 * side by side and the photo is fitted into its box.
 */
import {
  computed,
  defineAsyncComponent,
  inject,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  toRef,
  watch,
} from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { urls } from '@/api/endpoints'
import { ApiError } from '@/api/http'
import BaseButton from '@/components/BaseButton.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import ModalDialog from '@/components/ModalDialog.vue'
import { useUpdateAlbum } from '@/composables/useAlbums'
import { useDownload } from '@/composables/useDownload'
import {
  usePhotoQuery,
  useRegenerateDescription,
  useUpdateDescription,
} from '@/composables/usePhotos'
import { useRendition } from '@/composables/useRendition'
import { useNotificationsStore } from '@/stores/notifications'
import { formatBytes, formatDateTime } from '@/utils/format'
import { photoNavigationKey } from './galleryNavigation'

const props = defineProps<{ id: string }>()

// Loaded on first use: keeps the form-validation libraries out of the viewer's initial download.
const EditDescriptionForm = defineAsyncComponent(() => import('./EditDescriptionForm.vue'))
const AddToAlbumDialog = defineAsyncComponent(
  () => import('@/features/albums/AddToAlbumDialog.vue'),
)

const router = useRouter()
const route = useRoute()
const navigation = inject(photoNavigationKey, null)
const notifications = useNotificationsStore()

const { data: photo, isPending, error, refetch } = usePhotoQuery(toRef(props, 'id'))
const updateDescription = useUpdateDescription()
const regenerate = useRegenerateDescription()
const { state: downloadState, download } = useDownload()

const editing = ref(false)
const addingToAlbum = ref(false)
const album = computed(() => navigation?.album?.value)
const updateAlbum = useUpdateAlbum()
const previewUrl = computed(() => urls.preview(props.id, photo.value?.renditionVersion ?? 0))
const { status: previewStatus, handleImageError } = useRendition(
  previewUrl,
  toRef(() => photo.value?.renditionState),
)

const open = computed({
  get: () => true,
  set: (value: boolean) => {
    if (!value) {
      void router.push(navigation?.closeRoute() ?? { name: 'gallery', query: route.query })
    }
  },
})

const index = computed(() => navigation?.ids.value.indexOf(props.id) ?? -1)
const previousId = computed(() =>
  index.value > 0 ? navigation?.ids.value[index.value - 1] : undefined,
)
const nextId = computed(() =>
  navigation && index.value >= 0 ? navigation.ids.value[index.value + 1] : undefined,
)

const dialog = ref<{ focus: () => void } | null>(null)

watch(
  () => props.id,
  async () => {
    editing.value = false
    // Reaching the first or last photo disables Previous or Next; if that button had focus, the browser drops it
    // on the page body. Keep it in the viewer.
    await nextTick()
    if (!document.activeElement || document.activeElement === document.body) dialog.value?.focus()
  },
)

function go(id: string | undefined): void {
  if (id) {
    void router.replace(
      navigation?.photoRoute(id) ?? { name: 'photo', params: { id }, query: route.query },
    )
  }
  if (navigation && index.value >= navigation.ids.value.length - 3) {
    void navigation.loadMore()
  }
}

// Listened to on the document while the viewer is open, so the arrows work wherever focus is in the dialog (the
// close button gets focus first; Previous/Next live in the footer). Text fields keep their own arrow keys.
function onKeydown(event: KeyboardEvent): void {
  if (
    editing.value ||
    addingToAlbum.value ||
    event.defaultPrevented ||
    event.altKey ||
    event.ctrlKey ||
    event.metaKey ||
    event.target instanceof HTMLTextAreaElement ||
    event.target instanceof HTMLInputElement ||
    event.target instanceof HTMLSelectElement ||
    (event.target instanceof HTMLElement && event.target.isContentEditable)
  ) {
    return
  }

  if (event.key === 'ArrowLeft') go(previousId.value)
  if (event.key === 'ArrowRight') go(nextId.value)
}

onMounted(() => document.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => document.removeEventListener('keydown', onKeydown))

// A mostly horizontal one-finger swipe on the photo goes to the next (swipe left) or previous (swipe right)
// photo. Pinching (two fingers) and panning a page the user has zoomed into are left to the browser.
const SWIPE_DISTANCE = 50
let swipeStart: { x: number; y: number } | null = null

function onTouchStart(event: TouchEvent): void {
  const touch = event.touches[0]
  swipeStart =
    event.touches.length === 1 && touch && (window.visualViewport?.scale ?? 1) <= 1
      ? { x: touch.clientX, y: touch.clientY }
      : null
}

function onTouchEnd(event: TouchEvent): void {
  const touch = event.changedTouches[0]
  const start = swipeStart
  swipeStart = null
  if (!start || !touch || editing.value || addingToAlbum.value) return

  const dx = touch.clientX - start.x
  const dy = touch.clientY - start.y
  if (Math.abs(dx) >= SWIPE_DISTANCE && Math.abs(dx) > 2 * Math.abs(dy)) {
    go(dx < 0 ? nextId.value : previousId.value)
  }
}

async function save(description: string): Promise<void> {
  try {
    await updateDescription.mutateAsync({ id: props.id, description })
    editing.value = false
    notifications.notify('success', 'Description saved.')
  } catch (e) {
    notifications.notify('error', e instanceof ApiError ? e.message : 'Saving failed.')
  }
}

async function useAsCover(): Promise<void> {
  if (!album.value) return
  try {
    await updateAlbum.mutateAsync({ id: album.value.id, coverPhotoId: props.id })
    notifications.notify('success', `This photo is now the cover of “${album.value.name}”.`)
  } catch (e) {
    notifications.notify(
      'error',
      e instanceof ApiError ? e.message : 'The cover could not be changed.',
    )
  }
}

async function describeAgain(): Promise<void> {
  try {
    await regenerate.mutateAsync(props.id)
    notifications.notify('info', 'A new description is being generated.')
  } catch (e) {
    notifications.notify('error', e instanceof ApiError ? e.message : 'Request failed.')
  }
}
</script>

<template>
  <ModalDialog
    ref="dialog"
    v-model:open="open"
    :title="photo?.description || photo?.fileName || 'Photo'"
    size="full"
  >
    <div class="flex flex-col gap-4 lg:h-full lg:flex-row">
      <div v-if="isPending" class="flex min-h-48 flex-1 items-center justify-center">
        <BaseSpinner label="Loading photo" />
      </div>
      <ErrorAlert
        v-else-if="error"
        :message="error.message"
        retry-label="Retry"
        @retry="refetch()"
      />
      <template v-else-if="photo">
        <!-- Edge to edge when the dialog is full screen (anything not `roomy`, see src/styles/main.css). -->
        <div
          class="relative -mx-4 -mt-4 flex min-h-48 touch-pan-y touch-pinch-zoom items-center justify-center bg-black roomy:mx-0 roomy:mt-0 roomy:rounded-md lg:mr-0 lg:min-h-0 lg:flex-1"
          @touchstart.passive="onTouchStart"
          @touchend.passive="onTouchEnd"
          @touchcancel.passive="swipeStart = null"
        >
          <img
            v-if="previewStatus === 'ready'"
            :src="previewUrl"
            :alt="photo.description || `Photo ${photo.fileName}`"
            class="block max-h-[70dvh] max-w-full object-contain lg:absolute lg:inset-0 lg:size-full lg:max-h-none"
            @error="handleImageError"
          />
          <p v-else class="p-6 text-sm text-white">
            {{ previewStatus === 'pending' ? 'Preparing preview...' : 'Preview unavailable.' }}
          </p>
        </div>

        <aside
          class="flex w-full flex-col gap-4 lg:w-80 lg:shrink-0 lg:overflow-y-auto"
          aria-label="Photo details"
        >
          <section>
            <EditDescriptionForm
              v-if="editing"
              :initial="photo.description"
              :saving="updateDescription.isPending.value"
              @save="save"
              @cancel="editing = false"
            />
            <template v-else>
              <h3 class="text-sm font-medium text-muted">Description</h3>
              <p class="mt-1">{{ photo.description || 'No description yet.' }}</p>
              <p v-if="photo.descriptionSource" class="mt-1 text-xs text-muted">
                {{
                  photo.descriptionSource === 'Gemini'
                    ? 'Generated by Gemini'
                    : 'Written by a person'
                }}
              </p>
              <p
                v-if="photo.descriptionState === 'Pending'"
                class="mt-1 text-xs text-muted"
                role="status"
              >
                Generating a new description...
              </p>
              <div class="mt-2 flex flex-wrap gap-2">
                <BaseButton @click="editing = true">Edit</BaseButton>
                <BaseButton
                  :loading="regenerate.isPending.value"
                  :disabled="photo.descriptionState === 'Pending'"
                  @click="describeAgain"
                  >Describe with AI</BaseButton
                >
              </div>
            </template>
          </section>

          <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
            <dt class="text-muted">File</dt>
            <dd class="truncate" :title="photo.fileName">{{ photo.fileName }}</dd>
            <template v-if="photo.takenAt"
              ><dt class="text-muted">Taken</dt>
              <dd>{{ formatDateTime(photo.takenAt) }}</dd></template
            >
            <dt class="text-muted">Uploaded</dt>
            <dd>{{ formatDateTime(photo.uploadedAt) }}</dd>
            <template v-if="photo.width"
              ><dt class="text-muted">Size</dt>
              <dd>
                {{ photo.width }} x {{ photo.height }} {{ formatBytes(photo.sizeBytes) }}
              </dd></template
            >
            <template v-if="photo.tags.length"
              ><dt class="text-muted">Tags</dt>
              <dd>{{ photo.tags.join(', ') }}</dd></template
            >
          </dl>

          <div class="flex flex-col gap-2">
            <BaseButton
              variant="primary"
              :loading="downloadState === 'preparing'"
              @click="download([photo.id])"
            >
              {{ downloadState === 'preparing' ? 'Decrypting original...' : 'Download original' }}
            </BaseButton>
            <BaseButton @click="addingToAlbum = true">Add to album</BaseButton>
            <template v-if="album">
              <p v-if="album.cover?.id === photo.id" class="text-center text-sm text-muted">
                This is the album's cover.
              </p>
              <BaseButton v-else :loading="updateAlbum.isPending.value" @click="useAsCover"
                >Use as album cover</BaseButton
              >
            </template>
          </div>
        </aside>
      </template>
    </div>
    <AddToAlbumDialog v-if="addingToAlbum" v-model:open="addingToAlbum" :photo-ids="[props.id]" />
    <template #footer>
      <BaseButton class="flex-1 sm:flex-none" :disabled="!previousId" @click="go(previousId)"
        >Previous</BaseButton
      >
      <BaseButton class="flex-1 sm:flex-none" :disabled="!nextId" @click="go(nextId)"
        >Next</BaseButton
      >
    </template>
  </ModalDialog>
</template>
