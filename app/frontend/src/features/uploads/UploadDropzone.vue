<script setup lang="ts">
/**
 * Drag-and-drop target that is also a normal file input (keyboard and screen-reader accessible: the visible
 * button is a <label> for the hidden input).
 */
import { ref, useId } from 'vue'
import { ACCEPTED_TYPES } from '@/stores/uploads'

const emit = defineEmits<{ files: [files: File[]] }>()
const dragging = ref(false)
const inputId = useId()
const accept = ACCEPTED_TYPES.join(',')

function onDrop(event: DragEvent): void {
  dragging.value = false
  const files = Array.from(event.dataTransfer?.files ?? [])
  if (files.length > 0) {
    emit('files', files)
  }
}

function onChange(event: Event): void {
  const input = event.target as HTMLInputElement
  emit('files', Array.from(input.files ?? []))
  input.value = ''
}
</script>

<template>
  <!-- Touch screens cannot drag files in: there the button leads, with less padding. -->
  <div
    class="rounded-lg border-2 border-dashed p-10 text-center transition-colors pointer-coarse:p-6"
    :class="dragging ? 'border-brand bg-brand/5' : 'border-border'"
    @dragenter.prevent="dragging = true"
    @dragover.prevent
    @dragleave.prevent="dragging = false"
    @drop.prevent="onDrop"
  >
    <p class="text-base font-medium pointer-coarse:hidden">Drag photos here</p>
    <p class="mt-1 text-sm text-muted">JPEG, PNG, GIF or WebP, up to 100 MB each</p>
    <!-- HEIC needs a native codec, which the workers deliberately do not ship (docs/adr/0007). -->
    <p class="mt-1 text-sm text-muted">iPhone HEIC photos: share or export them as JPEG first.</p>
    <label
      :for="inputId"
      class="mt-4 inline-flex cursor-pointer rounded-md bg-brand px-4 py-2 text-sm font-medium text-on-brand hover:bg-brand-hover focus-within:outline-3 pointer-coarse:px-6 pointer-coarse:py-3 pointer-coarse:text-base"
    >
      Choose photos
      <input
        :id="inputId"
        type="file"
        multiple
        :accept="accept"
        class="sr-only"
        @change="onChange"
      />
    </label>
  </div>
</template>
