<script setup lang="ts">
/**
 * Photo viewer (nested route /gallery/photos/:id, so photos are deep-linkable). Shows the large preview,
 * metadata, description editing, AI re-description and original download. Left/right arrows navigate.
 */
import {
  computed,
  defineAsyncComponent,
  inject,
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
import { useDownload } from '@/composables/useDownload'
import {
  usePhotoQuery,
  useRegenerateDescription,
  useUpdateDescription,
} from '@/composables/usePhotos'
import { useRendition } from '@/composables/useRendition'
import { useNotificationsStore } from '@/stores/notifications'
import { formatBytes, formatDateTime } from '@/utils/format'
import { galleryNavigationKey } from './galleryNavigation'

const props = defineProps<{ id: string }>()

// Loaded on first edit: keeps the form-validation libraries out of the viewer's initial download.
const EditDescriptionForm = defineAsyncComponent(() => import('./EditDescriptionForm.vue'))

const router = useRouter()
const route = useRoute()
const navigation = inject(galleryNavigationKey, null)
const notifications = useNotificationsStore()

const { data: photo, isPending, error, refetch } = usePhotoQuery(toRef(props, 'id'))
const updateDescription = useUpdateDescription()
const regenerate = useRegenerateDescription()
const { state: downloadState, download } = useDownload()

const editing = ref(false)
const previewUrl = computed(() => urls.preview(props.id, photo.value?.renditionVersion ?? 0))
const { status: previewStatus, handleImageError } = useRendition(
  previewUrl,
  toRef(() => photo.value?.renditionState),
)

const open = computed({
  get: () => true,
  set: (value: boolean) => {
    if (!value) {
      void router.push({ name: 'gallery', query: route.query })
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

watch(
  () => props.id,
  () => (editing.value = false),
)

function go(id: string | undefined): void {
  if (id) {
    void router.replace({ name: 'photo', params: { id }, query: route.query })
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

async function save(description: string): Promise<void> {
  try {
    await updateDescription.mutateAsync({ id: props.id, description })
    editing.value = false
    notifications.notify('success', 'Description saved.')
  } catch (e) {
    notifications.notify('error', e instanceof ApiError ? e.message : 'Saving failed.')
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
    v-model:open="open"
    :title="photo?.description || photo?.fileName || 'Photo'"
    size="full"
  >
    <div class="flex h-full flex-col gap-4 lg:flex-row">
      <div v-if="isPending" class="flex flex-1 items-center justify-center">
        <BaseSpinner label="Loading photo" />
      </div>
      <ErrorAlert
        v-else-if="error"
        :message="error.message"
        retry-label="Retry"
        @retry="refetch()"
      />
      <template v-else-if="photo">
        <div class="flex min-h-64 flex-1 items-center justify-center rounded-md bg-black">
          <img
            v-if="previewStatus === 'ready'"
            :src="previewUrl"
            :alt="photo.description || `Photo ${photo.fileName}`"
            class="max-h-[75vh] max-w-full object-contain"
            @error="handleImageError"
          />
          <p v-else class="p-6 text-sm text-white">
            {{ previewStatus === 'pending' ? 'Preparing preview...' : 'Preview unavailable.' }}
          </p>
        </div>

        <aside class="flex w-full flex-col gap-4 lg:w-80" aria-label="Photo details">
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

          <BaseButton
            variant="primary"
            :loading="downloadState === 'preparing'"
            @click="download([photo.id])"
          >
            {{ downloadState === 'preparing' ? 'Decrypting original...' : 'Download original' }}
          </BaseButton>
        </aside>
      </template>
    </div>
    <template #footer>
      <BaseButton :disabled="!previousId" @click="go(previousId)">Previous</BaseButton>
      <BaseButton :disabled="!nextId" @click="go(nextId)">Next</BaseButton>
    </template>
  </ModalDialog>
</template>
