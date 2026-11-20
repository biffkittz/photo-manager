<script setup lang="ts">
/** Search input with an accessible label; v-model updates immediately, the parent debounces. */
import { useId } from 'vue'

const query = defineModel<string>({ required: true })
const id = useId()
</script>

<template>
  <div role="search" class="w-full max-w-md">
    <label :for="id" class="sr-only">Search photos</label>
    <input
      :id="id"
      v-model="query"
      type="search"
      maxlength="200"
      autocomplete="off"
      placeholder="Search descriptions and filenames (e.g. beach sunset)"
      class="w-full rounded-md border border-border bg-surface px-3 py-2 text-sm placeholder:text-muted"
    />
  </div>
</template>
