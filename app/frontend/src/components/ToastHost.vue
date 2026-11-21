<script setup lang="ts">
/**
 * Renders notifications in a polite live region so screen readers announce them. On phones they span the width
 * and float above the bottom bars (photo viewer's Previous/Next, the two-row selection bar) instead of covering them.
 */
import { storeToRefs } from 'pinia'
import { useNotificationsStore } from '@/stores/notifications'

const store = useNotificationsStore()
const { items } = storeToRefs(store)
</script>

<template>
  <div
    aria-live="polite"
    aria-atomic="false"
    class="pointer-events-none fixed inset-x-4 bottom-32 z-50 flex flex-col gap-2 sm:bottom-4 sm:left-auto sm:w-80"
  >
    <div
      v-for="item in items"
      :key="item.id"
      class="pointer-events-auto flex items-start justify-between gap-3 rounded-md border border-border bg-surface py-1 pl-3 text-sm shadow-lg"
      :class="{
        'border-danger/60': item.kind === 'error',
        'border-success/60': item.kind === 'success',
      }"
    >
      <p class="py-2">{{ item.message }}</p>
      <button
        type="button"
        class="flex size-9 shrink-0 items-center justify-center text-muted hover:text-text pointer-coarse:size-11"
        aria-label="Dismiss notification"
        @click="store.dismiss(item.id)"
      >
        <span aria-hidden="true" class="text-lg leading-none">&times;</span>
      </button>
    </div>
  </div>
</template>
