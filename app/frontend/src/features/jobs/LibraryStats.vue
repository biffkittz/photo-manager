<script setup lang="ts">
import { computed } from 'vue'
import type { LibraryOverview } from '@/api/types'

const props = defineProps<{ overview: LibraryOverview }>()

const stats = computed(() => [
  { label: 'Photos', value: props.overview.totalPhotos },
  { label: 'Without thumbnails', value: props.overview.missingRenditions },
  { label: 'Without descriptions', value: props.overview.missingDescriptions },
  { label: 'Failed uploads', value: props.overview.failedUploads },
  { label: 'Missing from archive', value: props.overview.missingFromArchive },
])
</script>

<template>
  <dl class="grid grid-cols-2 gap-3 sm:grid-cols-5">
    <div
      v-for="stat in stats"
      :key="stat.label"
      class="rounded-md border border-border bg-surface p-3"
    >
      <dt class="text-xs text-muted">{{ stat.label }}</dt>
      <dd class="text-2xl font-semibold">{{ stat.value.toLocaleString() }}</dd>
    </div>
  </dl>
</template>
