<script setup lang="ts">
/** Page title (focus target after navigation) with breadcrumbs taken from route meta. */
import { computed } from 'vue'
import { useRoute } from 'vue-router'

defineProps<{ title: string }>()
defineSlots<{ actions?(): unknown }>()
const route = useRoute()
const crumbs = computed(() => route.meta.breadcrumb ?? [])
</script>

<template>
  <div class="mb-6 flex flex-wrap items-end justify-between gap-4">
    <div>
      <nav v-if="crumbs.length > 1" aria-label="Breadcrumb" class="mb-1 text-sm text-muted">
        <ol class="flex gap-2">
          <li v-for="(crumb, index) in crumbs" :key="crumb.label" class="flex gap-2">
            <RouterLink
              v-if="crumb.to && index < crumbs.length - 1"
              :to="crumb.to"
              class="hover:underline"
              >{{ crumb.label }}</RouterLink
            >
            <span v-else aria-current="page">{{ crumb.label }}</span>
            <span v-if="index < crumbs.length - 1" aria-hidden="true">/</span>
          </li>
        </ol>
      </nav>
      <h1 data-page-title tabindex="-1" class="text-2xl font-bold">{{ title }}</h1>
    </div>
    <div class="flex flex-wrap gap-2"><slot name="actions" /></div>
  </div>
</template>
