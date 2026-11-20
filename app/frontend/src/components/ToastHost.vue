<script setup lang="ts">
/** Renders notifications in a polite live region so screen readers announce them. */
import { storeToRefs } from 'pinia'
import { useNotificationsStore } from '@/stores/notifications'

const store = useNotificationsStore()
const { items } = storeToRefs(store)
</script>

<template>
  <div
    aria-live="polite"
    aria-atomic="false"
    class="pointer-events-none fixed right-4 bottom-4 z-50 flex w-80 flex-col gap-2"
  >
    <div
      v-for="item in items"
      :key="item.id"
      class="pointer-events-auto flex items-start justify-between gap-3 rounded-md border border-border bg-surface p-3 text-sm shadow-lg"
      :class="{
        'border-danger/60': item.kind === 'error',
        'border-success/60': item.kind === 'success',
      }"
    >
      <p>{{ item.message }}</p>
      <button
        type="button"
        class="text-muted hover:text-text"
        aria-label="Dismiss notification"
        @click="store.dismiss(item.id)"
      >
        <span aria-hidden="true">&times;</span>
      </button>
    </div>
  </div>
</template>
