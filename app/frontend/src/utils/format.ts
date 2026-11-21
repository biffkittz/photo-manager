import type { GalleryPeriod } from '@/api/types'

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' })
const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
})

/** Formats an ISO timestamp as a date ("Jul 4, 2024"); empty for null. */
export function formatDate(iso: string | null | undefined): string {
  return iso ? dateFormat.format(new Date(iso)) : ''
}

/** Formats an ISO timestamp with time. */
export function formatDateTime(iso: string | null | undefined): string {
  return iso ? dateTimeFormat.format(new Date(iso)) : ''
}

/** Formats a byte count ("3.2 MB"). */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) {
    return ''
  }

  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }

  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}

// Months and years of the timeline are calendar values, not instants: formatted in UTC so nothing shifts them.
const monthYearFormat = new Intl.DateTimeFormat(undefined, {
  month: 'long',
  year: 'numeric',
  timeZone: 'UTC',
})
const shortMonthFormat = new Intl.DateTimeFormat(undefined, { month: 'short', timeZone: 'UTC' })
const localMonthYearFormat = new Intl.DateTimeFormat(undefined, { month: 'short', year: 'numeric' })
const localMonthFormat = new Intl.DateTimeFormat(undefined, { month: 'short' })
const countFormat = new Intl.NumberFormat()

/** "July 2019", or "2019" for a whole year. */
export function formatPeriod(period: GalleryPeriod): string {
  return period.month
    ? monthYearFormat.format(Date.UTC(period.year, period.month - 1, 1))
    : String(period.year)
}

/** A month's short name ("Jul"). */
export function formatMonthShort(month: number): string {
  return shortMonthFormat.format(Date.UTC(2000, month - 1, 1))
}

/** The months an album spans: "Jul 2019", "Jul – Aug 2019", "Dec 2018 – Jan 2019"; empty without dates. */
export function formatMonthRange(first: string | null, last: string | null): string {
  if (!first || !last) return ''
  const from = new Date(first)
  const to = new Date(last)
  if (from.getFullYear() !== to.getFullYear()) {
    return `${localMonthYearFormat.format(from)} – ${localMonthYearFormat.format(to)}`
  }

  return from.getMonth() === to.getMonth()
    ? localMonthYearFormat.format(from)
    : `${localMonthFormat.format(from)} – ${localMonthYearFormat.format(to)}`
}

/** "1 photo", "1,204 photos". */
export function formatPhotoCount(count: number): string {
  return `${countFormat.format(count)} ${count === 1 ? 'photo' : 'photos'}`
}

/** "1 year ago", "7 years ago". */
export function formatYearsAgo(years: number): string {
  return years === 1 ? '1 year ago' : `${years} years ago`
}
