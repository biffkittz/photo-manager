<script setup lang="ts">
/** Admin maintenance page: library health, start long-running jobs (Temporal workflows), watch progress. */
import { ref } from 'vue'
import { ApiError } from '@/api/http'
import type { JobKind } from '@/api/types'
import BaseButton from '@/components/BaseButton.vue'
import BaseSpinner from '@/components/BaseSpinner.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import ModalDialog from '@/components/ModalDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useCancelJob, useJobsQuery, useOverviewQuery, useStartJob } from '@/composables/useJobs'
import { useNotificationsStore } from '@/stores/notifications'
import JobList from './JobList.vue'
import LibraryStats from './LibraryStats.vue'

const notifications = useNotificationsStore()
const jobs = useJobsQuery()
const overview = useOverviewQuery()
const startJob = useStartJob()
const cancelJob = useCancelJob()

const actions: { kind: JobKind; title: string; description: string }[] = [
  {
    kind: 'ReindexArchive',
    title: 'Re-index archive',
    description:
      'Scan the Wasabi bucket and import photos added outside the app. Also runs nightly.',
  },
  {
    kind: 'BackfillRenditions',
    title: 'Generate missing thumbnails',
    description: 'Decrypt archived photos on the workers and create thumbnails and previews.',
  },
  {
    kind: 'BackfillDescriptions',
    title: 'Generate missing descriptions',
    description:
      'Ask Gemini to describe photos that have no description yet (uses Gemini API quota).',
  },
]

const pending = ref<(typeof actions)[number] | null>(null)
const confirmOpen = ref(false)
const cancelling = ref<string | null>(null)

// Re-index only: by default a scan never marks more than a safe share of the library missing (protecting
// against a broken bucket listing). After deliberately deleting many files, an admin can override that.
const forceSweep = ref(false)

function confirm(action: (typeof actions)[number]): void {
  pending.value = action
  forceSweep.value = false
  confirmOpen.value = true
}

async function start(): Promise<void> {
  if (!pending.value) return
  try {
    await startJob.mutateAsync({
      kind: pending.value.kind,
      force: pending.value.kind === 'ReindexArchive' && forceSweep.value,
    })
    notifications.notify('success', `${pending.value.title} started.`)
  } catch (e) {
    notifications.notify(
      'error',
      e instanceof ApiError ? e.message : 'The job could not be started.',
    )
  } finally {
    confirmOpen.value = false
  }
}

async function cancel(id: string): Promise<void> {
  cancelling.value = id
  try {
    await cancelJob.mutateAsync(id)
    notifications.notify('info', 'Cancellation requested.')
  } catch (e) {
    notifications.notify('error', e instanceof ApiError ? e.message : 'Cancellation failed.')
  } finally {
    cancelling.value = null
  }
}
</script>

<template>
  <section class="flex flex-col gap-8">
    <PageHeader title="Actions" />

    <div>
      <h2 class="mb-3 text-lg font-semibold">Library</h2>
      <LibraryStats v-if="overview.data.value" :overview="overview.data.value" />
      <BaseSpinner v-else-if="overview.isPending.value" label="Loading library statistics" />
    </div>

    <div>
      <h2 class="mb-3 text-lg font-semibold">Maintenance</h2>
      <ul class="grid gap-3 md:grid-cols-3">
        <li
          v-for="action in actions"
          :key="action.kind"
          class="flex flex-col justify-between gap-3 rounded-md border border-border bg-surface p-4"
        >
          <div>
            <h3 class="font-medium">{{ action.title }}</h3>
            <p class="mt-1 text-sm text-muted">{{ action.description }}</p>
          </div>
          <BaseButton
            variant="primary"
            :aria-label="`Start: ${action.title}`"
            @click="confirm(action)"
            >Start</BaseButton
          >
        </li>
      </ul>
    </div>

    <div>
      <h2 class="mb-3 text-lg font-semibold">Recent jobs</h2>
      <BaseSpinner v-if="jobs.isPending.value" label="Loading jobs" />
      <ErrorAlert
        v-else-if="jobs.error.value"
        :message="jobs.error.value.message"
        retry-label="Retry"
        @retry="jobs.refetch()"
      />
      <p v-else-if="!jobs.data.value?.length" class="text-sm text-muted">No jobs have run yet.</p>
      <JobList v-else :jobs="jobs.data.value" :cancelling="cancelling" @cancel="cancel" />
    </div>

    <ModalDialog v-model:open="confirmOpen" :title="pending?.title ?? 'Start job'">
      <p class="text-sm">{{ pending?.description }}</p>
      <label v-if="pending?.kind === 'ReindexArchive'" class="mt-4 flex items-start gap-2 text-sm">
        <input v-model="forceSweep" type="checkbox" class="mt-1" />
        <span>
          Mark missing even if many
          <span class="block text-muted">
            Normally a scan refuses to mark more than about 10% of the library as missing, in case
            the bucket could not be read properly. Tick this only after deliberately deleting many
            files.
          </span>
        </span>
      </label>
      <template #footer>
        <BaseButton variant="ghost" @click="confirmOpen = false">Cancel</BaseButton>
        <BaseButton variant="primary" :loading="startJob.isPending.value" @click="start"
          >Start job</BaseButton
        >
      </template>
    </ModalDialog>
  </section>
</template>
