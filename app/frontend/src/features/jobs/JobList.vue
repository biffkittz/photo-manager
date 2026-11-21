<script setup lang="ts">
import type { JobKind, JobResponse, JobStatus } from '@/api/types'
import BaseButton from '@/components/BaseButton.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { formatDateTime } from '@/utils/format'

defineProps<{ jobs: JobResponse[]; cancelling: string | null }>()
const emit = defineEmits<{ cancel: [id: string] }>()

const kindLabels: Record<JobKind, string> = {
  ReindexArchive: 'Re-index archive',
  BackfillRenditions: 'Generate thumbnails',
  BackfillDescriptions: 'Generate descriptions',
}
const tones: Record<JobStatus, 'neutral' | 'info' | 'success' | 'danger'> = {
  Queued: 'neutral',
  Running: 'info',
  Completed: 'success',
  Failed: 'danger',
  Cancelled: 'neutral',
}
</script>

<template>
  <!-- Phones: one card per job (a six-column table does not fit). Wider screens: the table. -->
  <ul class="flex flex-col gap-2 sm:hidden" aria-label="Recent maintenance jobs">
    <li
      v-for="job in jobs"
      :key="job.id"
      class="flex flex-col gap-2 rounded-md border border-border bg-surface p-3 text-sm"
    >
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <p class="font-medium">{{ kindLabels[job.kind] }}</p>
          <p v-if="job.summary" class="text-xs text-muted">{{ job.summary }}</p>
        </div>
        <StatusBadge :tone="tones[job.status]" :text="job.status" />
      </div>
      <div class="flex items-center justify-between gap-3">
        <p class="text-muted">
          {{ job.processed.toLocaleString() }} processed<span v-if="job.failed" class="text-danger">
            ({{ job.failed }} failed)</span
          >, started {{ formatDateTime(job.startedAt ?? job.createdAt) }} by
          {{ job.requestedBy }}
        </p>
        <BaseButton
          v-if="job.status === 'Running' || job.status === 'Queued'"
          variant="ghost"
          class="shrink-0"
          :loading="cancelling === job.id"
          :aria-label="`Cancel ${kindLabels[job.kind]}`"
          @click="emit('cancel', job.id)"
          >Cancel</BaseButton
        >
      </div>
    </li>
  </ul>
  <div class="hidden overflow-x-auto sm:block">
    <table class="w-full text-left text-sm">
      <caption class="sr-only">
        Recent maintenance jobs
      </caption>
      <thead class="border-b border-border text-muted">
        <tr>
          <th scope="col" class="py-2 pr-3">Job</th>
          <th scope="col" class="py-2 pr-3">Status</th>
          <th scope="col" class="py-2 pr-3">Progress</th>
          <th scope="col" class="py-2 pr-3">Started</th>
          <th scope="col" class="py-2 pr-3">By</th>
          <th scope="col" class="py-2"><span class="sr-only">Actions</span></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="job in jobs" :key="job.id" class="border-b border-border/60 align-top">
          <td class="py-2 pr-3">
            {{ kindLabels[job.kind] }}
            <p v-if="job.summary" class="text-xs text-muted">{{ job.summary }}</p>
          </td>
          <td class="py-2 pr-3"><StatusBadge :tone="tones[job.status]" :text="job.status" /></td>
          <td class="py-2 pr-3">
            {{ job.processed.toLocaleString()
            }}<span v-if="job.failed" class="text-danger"> ({{ job.failed }} failed)</span>
          </td>
          <td class="py-2 pr-3">{{ formatDateTime(job.startedAt ?? job.createdAt) }}</td>
          <td class="py-2 pr-3">{{ job.requestedBy }}</td>
          <td class="py-2">
            <BaseButton
              v-if="job.status === 'Running' || job.status === 'Queued'"
              variant="ghost"
              :loading="cancelling === job.id"
              :aria-label="`Cancel ${kindLabels[job.kind]}`"
              @click="emit('cancel', job.id)"
              >Cancel</BaseButton
            >
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
