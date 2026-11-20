<script setup lang="ts">
/**
 * Accessible modal dialog (WAI-ARIA dialog pattern): rendered in <body> via Teleport so it escapes stacking
 * contexts, focus is trapped while open and restored on close, Escape and the backdrop close it.
 */
import { ref, useId } from 'vue'
import { useFocusTrap } from '@/composables/useFocusTrap'

withDefaults(defineProps<{ title: string; size?: 'md' | 'full' }>(), { size: 'md' })
const open = defineModel<boolean>('open', { required: true })
defineSlots<{ default(): unknown; footer?(): unknown }>()

const panel = ref<HTMLElement | null>(null)
const titleId = useId()
useFocusTrap(panel, open)

function close(): void {
  open.value = false
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-40 flex items-center justify-center bg-black/60 p-4"
      @click.self="close"
      @keydown.esc.stop="close"
    >
      <div
        ref="panel"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="titleId"
        tabindex="-1"
        class="flex max-h-full w-full flex-col overflow-hidden rounded-lg bg-surface shadow-xl"
        :class="size === 'full' ? 'h-full max-w-6xl' : 'max-w-lg'"
      >
        <header class="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 :id="titleId" class="truncate text-lg font-semibold">{{ title }}</h2>
          <button
            type="button"
            class="rounded p-1 text-muted hover:text-text"
            aria-label="Close dialog"
            @click="close"
          >
            <span aria-hidden="true" class="text-xl leading-none">&times;</span>
          </button>
        </header>
        <div class="min-h-0 flex-1 overflow-auto p-4"><slot /></div>
        <footer
          v-if="$slots.footer"
          class="flex justify-end gap-2 border-t border-border px-4 py-3"
        >
          <slot name="footer" />
        </footer>
      </div>
    </div>
  </Teleport>
</template>
