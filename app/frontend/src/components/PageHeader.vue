<script setup lang="ts">
/**
 * Page title (focus target after navigation) with breadcrumbs, taken from route meta unless the page passes its own
 * (an album's name is only known once it has loaded), and an optional line of details under the title.
 */
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { Breadcrumb } from '@/types/router'

const props = defineProps<{ title: string; breadcrumb?: Breadcrumb[] }>()
defineSlots<{ actions?(): unknown; details?(): unknown }>()
const route = useRoute()
const crumbs = computed(() => props.breadcrumb ?? route.meta.breadcrumb ?? [])
</script>

<template>
  <div class="mb-6 flex flex-wrap items-end justify-between gap-4">
    <div class="min-w-0">
      <nav v-if="crumbs.length > 1" aria-label="Breadcrumb" class="mb-1 text-sm text-muted">
        <ol class="flex min-w-0 gap-2">
          <li v-for="(crumb, index) in crumbs" :key="index" class="flex min-w-0 gap-2">
            <RouterLink
              v-if="crumb.to && index < crumbs.length - 1"
              :to="crumb.to"
              class="hover:underline"
              >{{ crumb.label }}</RouterLink
            >
            <span v-else aria-current="page" class="truncate">{{ crumb.label }}</span>
            <span v-if="index < crumbs.length - 1" aria-hidden="true">/</span>
          </li>
        </ol>
      </nav>
      <h1 data-page-title tabindex="-1" class="text-2xl font-bold break-words">{{ title }}</h1>
      <p v-if="$slots.details" class="mt-1 text-sm text-muted"><slot name="details" /></p>
    </div>
    <div class="flex flex-wrap gap-2"><slot name="actions" /></div>
  </div>
</template>
