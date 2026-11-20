<script setup lang="ts">
defineProps<{ title: string }>()
</script>

<template>
  <div class="rounded-lg border border-dashed border-border p-10 text-center">
    <p class="text-base font-semibold">{{ title }}</p>
    <div class="mt-1 text-sm text-muted"><slot /></div>
    <div v-if="$slots.action" class="mt-4"><slot name="action" /></div>
  </div>
</template>
