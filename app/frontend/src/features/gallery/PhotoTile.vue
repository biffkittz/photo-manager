<script setup lang="ts">
import { computed } from 'vue'
import type { PhotoSummary } from '@/api/types'
import PhotoThumbnail from '@/components/PhotoThumbnail.vue'
import { formatDate } from '@/utils/format'

const props = defineProps<{ photo: PhotoSummary; selected: boolean }>()
const emit = defineEmits<{ open: [id: string]; toggle: [id: string] }>()

const caption = computed(() => props.photo.description || props.photo.fileName)
const alt = computed(() => props.photo.description || `Photo ${props.photo.fileName}`)
</script>

<template>
  <figure class="group relative overflow-hidden rounded-md border border-border bg-surface-muted">
    <button
      type="button"
      class="block aspect-square w-full"
      :aria-label="`Open ${caption}`"
      @click="emit('open', photo.id)"
    >
      <PhotoThumbnail :photo="photo" :alt="alt" />
    </button>
    <figcaption class="truncate px-2 py-1 text-xs" :title="caption">
      {{ caption }}
      <span class="block text-muted">{{ formatDate(photo.takenAt ?? photo.uploadedAt) }}</span>
    </figcaption>
    <!-- The label is the tap area (40px, 44px on touch screens); the visible box stays small. -->
    <label class="group/select absolute top-0 left-0 flex p-2 pointer-coarse:p-1.5">
      <span
        class="flex rounded bg-surface/90 p-1 opacity-80 group-hover:opacity-100 group-focus-within/select:opacity-100 pointer-coarse:p-1.5"
      >
        <input
          type="checkbox"
          :checked="selected"
          class="size-4 pointer-coarse:size-5"
          @change="emit('toggle', photo.id)"
        />
      </span>
      <span class="sr-only">Select {{ caption }}</span>
    </label>
  </figure>
</template>
