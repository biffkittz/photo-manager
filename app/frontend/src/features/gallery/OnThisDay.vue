<script setup lang="ts">
/**
 * "On this day": one card per earlier year with photos taken on today's date ("7 years ago"). A card opens that
 * year's first photo; the viewer then steps through all of them, year after year.
 */
import { useId } from 'vue'
import type { OnThisDayYear } from '@/api/types'
import PhotoThumbnail from '@/components/PhotoThumbnail.vue'
import { formatPhotoCount, formatYearsAgo } from '@/utils/format'

defineProps<{ years: OnThisDayYear[] }>()
const emit = defineEmits<{ open: [id: string] }>()
const headingId = useId()
</script>

<template>
  <section :aria-labelledby="headingId">
    <h2 :id="headingId" class="mb-2 text-lg font-semibold">On this day</h2>
    <ul class="-mx-4 flex gap-3 overflow-x-auto px-4 pb-2 sm:mx-0 sm:px-0">
      <li v-for="year in years" :key="year.year" class="w-32 shrink-0 sm:w-40">
        <button
          v-if="year.photos[0]"
          type="button"
          class="group block w-full text-left"
          @click="emit('open', year.photos[0].id)"
        >
          <span
            class="block aspect-square overflow-hidden rounded-md border border-border bg-surface-muted"
          >
            <PhotoThumbnail :photo="year.photos[0]" />
          </span>
          <span class="mt-1 block text-sm font-medium group-hover:underline">{{
            formatYearsAgo(year.yearsAgo)
          }}</span>
          <span class="block text-xs text-muted"
            >{{ year.year }} · {{ formatPhotoCount(year.photos.length) }}</span
          >
        </button>
      </li>
    </ul>
  </section>
</template>
