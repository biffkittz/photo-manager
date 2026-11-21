<script setup lang="ts">
/**
 * A photo's square thumbnail, filling its box. Waits for (and asks a worker for) a thumbnail the photo does not
 * have yet; see useRendition. The alt text is empty by default: most callers label the surrounding control.
 */
import { computed, toRef } from 'vue'
import { urls } from '@/api/endpoints'
import type { PhotoSummary } from '@/api/types'
import { useRendition } from '@/composables/useRendition'

const props = withDefaults(defineProps<{ photo: PhotoSummary; alt?: string }>(), { alt: '' })

const src = computed(() => urls.thumbnail(props.photo.id, props.photo.renditionVersion))
const { status, handleImageError } = useRendition(
  src,
  toRef(() => props.photo.renditionState),
)
</script>

<template>
  <img
    v-if="status === 'ready'"
    :src="src"
    :alt="alt"
    loading="lazy"
    decoding="async"
    class="size-full object-cover"
    @error="handleImageError"
  />
  <span
    v-else
    class="flex size-full items-center justify-center p-2 text-center text-xs text-muted"
  >
    {{ status === 'pending' ? 'Preparing preview...' : 'Preview unavailable' }}
  </span>
</template>
