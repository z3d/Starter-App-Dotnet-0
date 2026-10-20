import { describe, expect, it } from 'vitest'
import { currentFbtYear, money, statusTone, words } from './format'

describe('format', () => {
  it('writes money as AUD, without cents unless asked', () => {
    expect(money(1234.5)).toBe('$1,235')
    expect(money('1234.5', { cents: true })).toBe('$1,234.50')
    expect(money(null)).toBe('—')
  })

  it('turns an enum name into words', () => {
    expect(words('CoolingOff')).toBe('Cooling off')
    expect(words('PublicBenevolentInstitution')).toBe('Public benevolent institution')
    expect(words('NSW')).toBe('NSW')
    expect(words('COOLING_OFF_RIGHTS')).toBe('Cooling off rights')
    expect(words('RFBA_IMPACT')).toBe('RFBA impact')
    expect(words('EARLY_TERMINATION_COSTS')).toBe('Early termination costs')
    expect(words(null)).toBe('—')
  })

  it('gives a status a tone that never carries the meaning alone', () => {
    expect(statusTone('Active')).toBe('positive')
    expect(statusTone('Applied')).toBe('caution')
    expect(statusTone('Declined')).toBe('negative')
    expect(statusTone('SomethingNew')).toBe('neutral')
  })

  it('names the FBT year by the year it ends in', () => {
    expect(currentFbtYear(new Date(2026, 9, 20))).toBe(2027)
    expect(currentFbtYear(new Date(2026, 2, 31))).toBe(2026)
    expect(currentFbtYear(new Date(2026, 3, 1))).toBe(2027)
  })
})
