<script setup lang="ts">
/**
 * Accessible button with visual variants and a loading state (aria-busy, disabled while loading). On touch
 * screens it is at least 44px tall (comfortable tap target).
 */
import { computed } from 'vue'
import BaseSpinner from './BaseSpinner.vue'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'

const props = withDefaults(
  defineProps<{
    variant?: Variant
    type?: 'button' | 'submit' | 'reset'
    loading?: boolean
    disabled?: boolean
  }>(),
  { variant: 'secondary', type: 'button', loading: false, disabled: false },
)

const classes = computed(() => {
  const base =
    'inline-flex items-center justify-center gap-2 rounded-md px-3 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60 pointer-coarse:min-h-11'
  const variants: Record<Variant, string> = {
    primary: 'bg-brand text-on-brand hover:bg-brand-hover',
    secondary: 'border border-border bg-surface text-text hover:bg-surface-muted',
    danger: 'bg-danger text-on-danger hover:opacity-90',
    ghost: 'text-text hover:bg-surface-muted',
  }
  return `${base} ${variants[props.variant]}`
})
</script>

<template>
  <button
    :type="type"
    :class="classes"
    :disabled="disabled || loading"
    :aria-busy="loading || undefined"
  >
    <BaseSpinner v-if="loading" size="sm" decorative />
    <slot />
  </button>
</template>
