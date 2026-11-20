<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ value: number; label: string }>()
const percent = computed(() => Math.round(Math.min(1, Math.max(0, props.value)) * 100))
</script>

<template>
  <div
    role="progressbar"
    :aria-label="label"
    aria-valuemin="0"
    aria-valuemax="100"
    :aria-valuenow="percent"
    class="h-2 w-full overflow-hidden rounded-full bg-surface-muted"
  >
    <div class="h-full bg-brand transition-[width]" :style="{ width: `${percent}%` }" />
  </div>
</template>
