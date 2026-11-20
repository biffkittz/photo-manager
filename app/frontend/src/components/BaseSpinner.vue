<script setup lang="ts">
withDefaults(defineProps<{ size?: 'sm' | 'md'; label?: string; decorative?: boolean }>(), {
  size: 'md',
  label: 'Loading',
  decorative: false,
})
</script>

<template>
  <span
    :role="decorative ? undefined : 'status'"
    :aria-hidden="decorative || undefined"
    class="inline-flex items-center"
  >
    <span
      class="animate-spin rounded-full border-2 border-current border-t-transparent"
      :class="size === 'sm' ? 'size-4' : 'size-6'"
    />
    <span v-if="!decorative" class="sr-only">{{ label }}</span>
  </span>
</template>
