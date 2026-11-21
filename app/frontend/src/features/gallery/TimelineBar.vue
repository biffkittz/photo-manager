<script setup lang="ts">
/**
 * The timeline: a row of years (newest first) and, once a year is chosen, a row of its months. Each is a link that
 * narrows the gallery to that year or month (?year=&month=, so it can be shared and the Back button undoes it).
 * The rows scroll sideways on phones and wrap on larger screens; the chosen year is scrolled into view.
 */
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, type RouteLocationRaw } from 'vue-router'
import type { GalleryPeriod, TimelineMonth } from '@/api/types'
import { periodQuery } from '@/utils/calendar'
import { formatMonthShort } from '@/utils/format'

const props = defineProps<{ months: TimelineMonth[]; period: GalleryPeriod | null }>()

const route = useRoute()
const years = computed(() => [...new Set(props.months.map((m) => m.year))])
const monthsOfYear = computed(() =>
  props.period ? props.months.filter((m) => m.year === props.period?.year) : [],
)

function link(period: GalleryPeriod | null): RouteLocationRaw {
  return { query: { ...route.query, ...periodQuery(period) } }
}

const chip =
  'block min-w-11 shrink-0 rounded-full border px-3 py-1 text-center text-sm whitespace-nowrap pointer-coarse:py-3'
function chipClass(current: boolean): string {
  return current
    ? `${chip} border-brand bg-brand text-on-brand`
    : `${chip} border-border bg-surface hover:bg-surface-muted`
}

const yearList = ref<HTMLElement | null>(null)
watch(
  [() => props.period?.year, () => props.months.length],
  async () => {
    await nextTick()
    const list = yearList.value
    const current = list?.querySelector<HTMLElement>('[aria-current="page"]')
    if (list && current && list.scrollWidth > list.clientWidth) {
      list.scrollLeft = current.offsetLeft - (list.clientWidth - current.offsetWidth) / 2
    }
  },
  { immediate: true },
)
</script>

<template>
  <nav aria-label="Timeline" class="flex flex-col gap-2">
    <ul
      ref="yearList"
      aria-label="Years"
      class="relative -mx-4 flex gap-2 overflow-x-auto px-4 pb-1 sm:mx-0 sm:flex-wrap sm:overflow-visible sm:px-0"
    >
      <li>
        <RouterLink
          :to="link(null)"
          :class="chipClass(!period)"
          :aria-current="!period ? 'page' : undefined"
          >All years</RouterLink
        >
      </li>
      <li v-for="year in years" :key="year">
        <RouterLink
          :to="link({ year })"
          :class="chipClass(period?.year === year)"
          :aria-current="period?.year === year && !period.month ? 'page' : undefined"
          >{{ year }}</RouterLink
        >
      </li>
    </ul>
    <ul
      v-if="period && monthsOfYear.length"
      :aria-label="`Months of ${period.year}`"
      class="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1 sm:mx-0 sm:flex-wrap sm:overflow-visible sm:px-0"
    >
      <li>
        <RouterLink
          :to="link({ year: period.year })"
          :class="chipClass(!period.month)"
          :aria-current="!period.month ? 'page' : undefined"
          >All of {{ period.year }}</RouterLink
        >
      </li>
      <li v-for="month in monthsOfYear" :key="month.month">
        <RouterLink
          :to="link({ year: period.year, month: month.month })"
          :class="chipClass(period.month === month.month)"
          :aria-current="period.month === month.month ? 'page' : undefined"
          >{{ formatMonthShort(month.month) }}</RouterLink
        >
      </li>
    </ul>
  </nav>
</template>
