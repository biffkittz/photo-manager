import type { LocationQuery } from 'vue-router'
import type { GalleryPeriod } from '@/api/types'

/**
 * The viewer's UTC offset in minutes east of UTC, sent as `offset` to the API so that years, months and "On this
 * day" follow the viewer's calendar, like the dates printed on the tiles.
 */
export function utcOffsetMinutes(date = new Date()): number {
  // getTimezoneOffset() is minutes *behind* UTC; `|| 0` turns UTC's -0 into 0.
  return -date.getTimezoneOffset() || 0
}

/** The viewer's date as yyyy-MM-dd. */
export function localDate(date = new Date()): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function whole(value: unknown, min: number, max: number): number | undefined {
  if (typeof value !== 'string' || !/^\d{1,4}$/.test(value)) return undefined
  const number = Number(value)
  return number >= min && number <= max ? number : undefined
}

/** The gallery's period from `?year=` and `?month=`; values that make no sense are ignored. */
export function periodFromQuery(query: LocationQuery): GalleryPeriod | null {
  const year = whole(query.year, 1, 9998)
  if (year === undefined) return null
  const month = whole(query.month, 1, 12)
  return month === undefined ? { year } : { year, month }
}

/** Query parameters for a period (undefined values remove them from the URL). */
export function periodQuery(period: GalleryPeriod | null): {
  year: string | undefined
  month: string | undefined
} {
  return {
    year: period ? String(period.year) : undefined,
    month: period?.month ? String(period.month) : undefined,
  }
}
