<script setup lang="ts" generic="T extends { id: string }">
/**
 * Generic, presentational responsive grid. Rendering of each item is delegated to the scoped `item` slot so the
 * grid can be reused for any item type (photos, upload previews).
 */
defineProps<{ items: T[]; density?: 'comfortable' | 'compact'; label: string }>()
defineSlots<{ item(props: { item: T; index: number }): unknown }>()
</script>

<template>
  <ul
    :aria-label="label"
    class="grid gap-3"
    :class="
      density === 'compact'
        ? 'grid-cols-3 sm:grid-cols-5 lg:grid-cols-8'
        : 'grid-cols-2 sm:grid-cols-3 lg:grid-cols-5'
    "
  >
    <li v-for="(item, index) in items" :key="item.id">
      <slot name="item" :item="item" :index="index" />
    </li>
  </ul>
</template>
