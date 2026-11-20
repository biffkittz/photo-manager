<script setup lang="ts">
import { storeToRefs } from 'pinia'
import BaseButton from '@/components/BaseButton.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useUploadQueue } from '@/composables/useUploadQueue'
import { useNotificationsStore } from '@/stores/notifications'
import { usePreferencesStore } from '@/stores/preferences'
import UploadDropzone from './UploadDropzone.vue'
import UploadQueueItem from './UploadQueueItem.vue'

const { items, summary, enqueue, cancel, retry, clearFinished } = useUploadQueue()
const notifications = useNotificationsStore()
// Remembered in this browser; applies to photos added from now on (already queued ones keep their setting).
const { describeUploads } = storeToRefs(usePreferencesStore())

function onFiles(files: File[]): void {
  enqueue(files, describeUploads.value)
}

async function onRetry(localId: string): Promise<void> {
  try {
    await retry(localId)
  } catch {
    notifications.notify(
      'error',
      'This upload can no longer be retried; please upload the file again.',
    )
  }
}
</script>

<template>
  <section>
    <PageHeader title="Uploads">
      <template #actions>
        <BaseButton :disabled="summary.total === summary.inFlight" @click="clearFinished"
          >Clear finished</BaseButton
        >
      </template>
    </PageHeader>

    <p class="mb-4 text-sm text-muted">
      Each photo is {{ describeUploads ? 'described by AI, ' : '' }}encrypted, and stored in the
      archive. You can leave this page; processing continues on the server.
    </p>

    <label class="mb-4 flex items-start gap-2 text-sm">
      <input v-model="describeUploads" type="checkbox" class="mt-1" />
      <span>
        Describe photos with AI (Gemini)
        <span class="block text-muted">
          Turn off to store photos without a description, for example when Gemini is busy: uploads
          then finish without waiting for it. They can be described later from the photo page or
          with "Generate missing descriptions".
        </span>
      </span>
    </label>

    <UploadDropzone @files="onFiles" />

    <p v-if="summary.total" class="mt-6 text-sm" role="status" aria-live="polite">
      {{ summary.done }} of {{ summary.total }} complete<span v-if="summary.failed"
        >, {{ summary.failed }} need attention</span
      >
    </p>
    <ul class="mt-3 flex flex-col gap-2" aria-label="Upload queue">
      <li v-for="item in items" :key="item.localId">
        <UploadQueueItem :item="item" @cancel="cancel" @retry="onRetry" />
      </li>
    </ul>
  </section>
</template>
