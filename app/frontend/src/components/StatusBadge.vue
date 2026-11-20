<script setup lang="ts">
import { computed } from 'vue'

type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'info'
const props = defineProps<{ tone: Tone; text: string }>()

const classes = computed(
  () =>
    ({
      neutral: 'bg-surface-muted text-muted',
      success: 'bg-success/15 text-success',
      warning: 'bg-warning/15 text-warning',
      danger: 'bg-danger/15 text-danger',
      info: 'bg-brand/15 text-brand',
    })[props.tone],
)
</script>

<template>
  <span class="inline-flex rounded-full px-2 py-0.5 text-xs font-medium" :class="classes">{{
    text
  }}</span>
</template>
