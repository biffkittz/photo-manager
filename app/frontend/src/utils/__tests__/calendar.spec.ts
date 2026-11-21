import { describe, expect, it } from 'vitest'
import { localDate, periodFromQuery, periodQuery, utcOffsetMinutes } from '../calendar'

describe('calendar', () => {
  it('reads a year and month from the address, ignoring anything that is not one', () => {
    expect(periodFromQuery({ year: '2019', month: '7' })).toEqual({ year: 2019, month: 7 })
    expect(periodFromQuery({ year: '2019' })).toEqual({ year: 2019 })
    expect(periodFromQuery({ year: '2019', month: '13' })).toEqual({ year: 2019 })
    expect(periodFromQuery({ month: '7' })).toBeNull()
    for (const year of ['0', '-1', '2019.5', '1e3', ' 2019', '99999', 'abc']) {
      expect(periodFromQuery({ year })).toBeNull()
    }
    expect(periodFromQuery({ year: ['2019', '2020'] })).toBeNull()
  })

  it('writes a period back to the address, removing what no longer applies', () => {
    expect(periodQuery({ year: 2019, month: 7 })).toEqual({ year: '2019', month: '7' })
    expect(periodQuery({ year: 2019 })).toEqual({ year: '2019', month: undefined })
    expect(periodQuery(null)).toEqual({ year: undefined, month: undefined })
  })

  it("gives the viewer's offset east of UTC and their date", () => {
    const date = new Date(2026, 2, 7, 23, 30)
    expect(utcOffsetMinutes(date)).toBe(-date.getTimezoneOffset() || 0)
    expect(Object.is(utcOffsetMinutes(date), -0)).toBe(false)
    expect(localDate(date)).toBe('2026-03-07')
  })
})
