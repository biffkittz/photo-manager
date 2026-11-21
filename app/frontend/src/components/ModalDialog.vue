<script setup lang="ts">
/**
 * Accessible modal dialog (WAI-ARIA dialog pattern): rendered in <body> via Teleport so it escapes stacking
 * contexts, focus is trapped while open and restored on close, Escape and the backdrop close it, and the page
 * behind does not scroll. `size="full"` (the photo viewer) fills the whole screen unless the screen is `roomy`
 * (src/styles/main.css), so phones get every pixel in either orientation.
 */
import { ref, useId } from 'vue'
import { useFocusTrap } from '@/composables/useFocusTrap'
import { useScrollLock } from '@/composables/useScrollLock'

withDefaults(defineProps<{ title: string; size?: 'md' | 'full' }>(), { size: 'md' })
const open = defineModel<boolean>('open', { required: true })
defineSlots<{ default(): unknown; footer?(): unknown }>()

const panel = ref<HTMLElement | null>(null)
const titleId = useId()
useFocusTrap(panel, open, close)
useScrollLock(open)

function close(): void {
  open.value = false
}

/** Moves focus to the dialog itself (for when the focused control went away, such as a button now disabled). */
function focus(): void {
  panel.value?.focus()
}

defineExpose({ focus })
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-40 flex items-center justify-center bg-black/60"
      :class="size === 'full' ? 'roomy:p-4' : 'p-4'"
      @click.self="close"
    >
      <div
        ref="panel"
        role="dialog"
        aria-modal="true"
        :aria-labelledby="titleId"
        tabindex="-1"
        class="flex max-h-full w-full flex-col overflow-hidden bg-surface shadow-xl"
        :class="size === 'full' ? 'h-full max-w-6xl roomy:rounded-lg' : 'max-w-lg rounded-lg'"
      >
        <header
          class="flex items-center justify-between gap-2 border-b border-border py-1.5 pr-1.5 pl-4"
        >
          <h2 :id="titleId" class="truncate text-lg font-semibold">{{ title }}</h2>
          <button
            type="button"
            class="flex size-11 shrink-0 items-center justify-center rounded text-muted hover:text-text"
            aria-label="Close dialog"
            @click="close"
          >
            <span aria-hidden="true" class="text-2xl leading-none">&times;</span>
          </button>
        </header>
        <div class="min-h-0 flex-1 overflow-auto overscroll-contain p-4"><slot /></div>
        <footer
          v-if="$slots.footer"
          class="flex justify-end gap-2 border-t border-border px-4 py-2 sm:py-3"
        >
          <slot name="footer" />
        </footer>
      </div>
    </div>
  </Teleport>
</template>
