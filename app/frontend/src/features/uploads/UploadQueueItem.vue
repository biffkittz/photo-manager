<script setup lang="ts">
import { computed } from 'vue'
import BaseButton from '@/components/BaseButton.vue'
import ProgressBar from '@/components/ProgressBar.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import type { UploadItem } from '@/stores/uploads'
import { formatBytes } from '@/utils/format'

const props = defineProps<{ item: UploadItem }>()
const emit = defineEmits<{ cancel: [localId: string]; retry: [localId: string] }>()

const badge = computed(() => {
  switch (props.item.phase) {
    case 'waiting':
      return { tone: 'neutral', text: 'Waiting' } as const
    case 'uploading':
      return { tone: 'info', text: 'Uploading' } as const
    case 'processing':
      return { tone: 'info', text: 'Processing' } as const
    case 'done':
      return { tone: 'success', text: 'Done' } as const
    case 'duplicate':
      return { tone: 'warning', text: 'Duplicate' } as const
    case 'cancelled':
      return { tone: 'neutral', text: 'Cancelled' } as const
    default:
      return {
        tone: 'danger',
        text: props.item.phase === 'rejected' ? 'Rejected' : 'Failed',
      } as const
  }
})
</script>

<template>
  <div class="flex flex-col gap-2 rounded-md border border-border bg-surface p-3">
    <div class="flex items-center justify-between gap-3">
      <div class="min-w-0">
        <p class="truncate text-sm font-medium" :title="item.fileName">{{ item.fileName }}</p>
        <p class="text-xs text-muted">{{ formatBytes(item.size) }}</p>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <StatusBadge :tone="badge.tone" :text="badge.text" />
        <BaseButton
          v-if="item.phase === 'waiting' || item.phase === 'uploading'"
          variant="ghost"
          :aria-label="`Cancel upload of ${item.fileName}`"
          @click="emit('cancel', item.localId)"
          >Cancel</BaseButton
        >
        <BaseButton
          v-if="item.phase === 'failed'"
          variant="ghost"
          :aria-label="`Retry upload of ${item.fileName}`"
          @click="emit('retry', item.localId)"
          >Retry</BaseButton
        >
      </div>
    </div>
    <ProgressBar
      v-if="item.phase === 'uploading'"
      :value="item.progress"
      :label="`Uploading ${item.fileName}`"
    />
    <p v-if="item.description" class="text-sm">{{ item.description }}</p>
    <p
      v-if="item.message"
      class="text-xs"
      :class="item.phase === 'failed' || item.phase === 'rejected' ? 'text-danger' : 'text-muted'"
    >
      {{ item.message }}
    </p>
  </div>
</template>
