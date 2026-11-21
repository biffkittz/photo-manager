import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import TimelineBar from '../TimelineBar.vue'

const months = [
  { year: 2024, month: 7, count: 1 },
  { year: 2024, month: 3, count: 4 },
  { year: 2015, month: 6, count: 9 },
]

async function mountAt(path: string, period: { year: number; month?: number } | null) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/gallery', component: { template: '<div />' } }],
  })
  await router.push(path)
  await router.isReady()
  return mount(TimelineBar, { props: { months, period }, global: { plugins: [router] } })
}

describe('TimelineBar', () => {
  it('offers each year once, newest first, as links that keep the search', async () => {
    const wrapper = await mountAt('/gallery?q=beach', null)
    const years = wrapper.get('[aria-label="Years"]').findAll('a')
    expect(years.map((a) => a.text())).toEqual(['All years', '2024', '2015'])
    expect(years[0]!.attributes('aria-current')).toBe('page')
    expect(years[1]!.attributes('href')).toBe('/gallery?q=beach&year=2024')
    expect(wrapper.find('[aria-label="Months of 2024"]').exists()).toBe(false)
  })

  it("shows the chosen year's months, and links back out of a month", async () => {
    const wrapper = await mountAt('/gallery?year=2024&month=7', { year: 2024, month: 7 })
    const monthLinks = wrapper.get('[aria-label="Months of 2024"]').findAll('a')
    expect(monthLinks.map((a) => a.text())).toEqual(['All of 2024', 'Jul', 'Mar'])
    expect(monthLinks[1]!.attributes('aria-current')).toBe('page')
    expect(monthLinks[0]!.attributes('href')).toBe('/gallery?year=2024')
    const allYears = wrapper.get('[aria-label="Years"]').find('a')
    expect(allYears.attributes('href')).toBe('/gallery')
  })
})
