<script setup lang="ts">
/** Inline error message, announced immediately to assistive technology. */
defineProps<{ message: string; retryLabel?: string }>()
const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <div
    role="alert"
    class="flex items-start justify-between gap-4 rounded-md border border-danger/40 bg-danger/10 p-3 text-sm"
  >
    <p>{{ message }}</p>
    <button v-if="retryLabel" type="button" class="font-medium underline" @click="emit('retry')">
      {{ retryLabel }}
    </button>
  </div>
</template>
