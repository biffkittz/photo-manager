<script setup lang="ts">
import { computed } from 'vue'
import type { AlbumSummary } from '@/api/types'
import PhotoThumbnail from '@/components/PhotoThumbnail.vue'
import { formatMonthRange, formatPhotoCount } from '@/utils/format'

const props = defineProps<{ album: AlbumSummary }>()
const details = computed(() =>
  [
    formatPhotoCount(props.album.photoCount),
    formatMonthRange(props.album.firstDate, props.album.lastDate),
  ]
    .filter(Boolean)
    .join(' · '),
)
</script>

<template>
  <RouterLink :to="{ name: 'album', params: { albumId: album.id } }" class="group block">
    <span
      class="block aspect-square overflow-hidden rounded-md border border-border bg-surface-muted"
    >
      <PhotoThumbnail v-if="album.cover" :photo="album.cover" />
      <span v-else class="flex size-full items-center justify-center text-sm text-muted"
        >No photos yet</span
      >
    </span>
    <span class="mt-2 block truncate font-medium group-hover:underline" :title="album.name">{{
      album.name
    }}</span>
    <span class="block text-sm text-muted">{{ details }}</span>
  </RouterLink>
</template>
