<script setup lang="ts">
/**
 * What can be done with the selected photos. Inline above the photos on larger screens; on phones it is pinned to
 * the bottom of the screen in two rows (count, Select all and Clear; then the actions, sharing the width), so it
 * stays in reach while scrolling. Pages pad their bottom (`max-sm:pb-32`) so it never covers the last photos.
 */
import BaseButton from './BaseButton.vue'

defineProps<{ count: number; selectingAll?: boolean }>()
const emit = defineEmits<{ selectAll: []; clear: [] }>()
defineSlots<{ default(): unknown }>()
</script>

<template>
  <div
    role="region"
    aria-label="Selection"
    class="flex flex-wrap items-center gap-2 max-sm:fixed max-sm:inset-x-0 max-sm:bottom-0 max-sm:z-30 max-sm:border-t max-sm:border-border max-sm:bg-surface max-sm:px-4 max-sm:py-3 max-sm:shadow-lg"
  >
    <span class="text-sm max-sm:mr-auto">{{ count }} selected</span>
    <div class="flex gap-2 max-sm:order-last max-sm:w-full max-sm:*:flex-1"><slot /></div>
    <BaseButton variant="ghost" :loading="selectingAll" @click="emit('selectAll')"
      >Select all</BaseButton
    >
    <BaseButton variant="ghost" @click="emit('clear')">Clear</BaseButton>
  </div>
</template>
