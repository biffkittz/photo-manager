import { describe, expect, it } from 'vitest'
import {
  formatMonthRange,
  formatMonthShort,
  formatPeriod,
  formatPhotoCount,
  formatYearsAgo,
} from '../format'

// Midday on the 15th, so no time zone moves these into another month.
const mid = (year: number, month: number) => new Date(year, month - 1, 15, 12).toISOString()

describe('format', () => {
  it('names a year or a month of the timeline', () => {
    expect(formatPeriod({ year: 2019 })).toBe('2019')
    expect(formatPeriod({ year: 2019, month: 7 })).toBe('July 2019')
    expect(formatMonthShort(12)).toBe('Dec')
  })

  it('describes the months an album spans as briefly as possible', () => {
    expect(formatMonthRange(mid(2019, 7), mid(2019, 7))).toBe('Jul 2019')
    expect(formatMonthRange(mid(2019, 7), mid(2019, 8))).toBe('Jul – Aug 2019')
    expect(formatMonthRange(mid(2018, 12), mid(2019, 1))).toBe('Dec 2018 – Jan 2019')
    expect(formatMonthRange(null, null)).toBe('')
  })

  it('counts photos and years', () => {
    expect(formatPhotoCount(1)).toBe('1 photo')
    expect(formatPhotoCount(0)).toBe('0 photos')
    expect(formatPhotoCount(1204)).toBe('1,204 photos')
    expect(formatYearsAgo(1)).toBe('1 year ago')
    expect(formatYearsAgo(7)).toBe('7 years ago')
  })
})
