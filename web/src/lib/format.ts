// Money, dates and enum names the way a person reads them.
const aud0 = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', maximumFractionDigits: 0 })
const aud2 = new Intl.NumberFormat('en-AU', { style: 'currency', currency: 'AUD', minimumFractionDigits: 2 })

export function money(value: number | string | null | undefined, options: { cents?: boolean } = {}) {
  if (value === null || value === undefined || value === '') return '—'
  return (options.cents ? aud2 : aud0).format(Number(value))
}

export function percent(value: number | string | null | undefined, digits = 1) {
  if (value === null || value === undefined || value === '') return '—'
  return `${Number(value).toFixed(digits)}%`
}

export function day(iso: string | null | undefined) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })
}

export function whenAt(iso: string | null | undefined) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('en-AU', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

const ACRONYMS = new Set([
  'RFBA',
  'FBT',
  'GST',
  'EV',
  'LCT',
  'HELP',
  'MLS',
  'ABN',
  'VIN',
  'ATO',
  'NSW',
  'VIC',
  'QLD',
  'SA',
  'WA',
  'TAS',
  'NT',
  'ACT',
])

// "CoolingOff" → "Cooling off"; "COOLING_OFF_RIGHTS" → "Cooling off rights"; "NSW" and "RFBA" stay.
export function words(name: string | null | undefined) {
  if (!name) return '—'
  if (name.includes('_')) {
    const tokens = name.split('_').filter(Boolean)
    return tokens
      .map((token, i) => {
        const acronym = ACRONYMS.has(token)
        const word = acronym ? token : token.toLowerCase()
        return i === 0 && !acronym ? word.charAt(0).toUpperCase() + word.slice(1) : word
      })
      .join(' ')
  }
  if (name === name.toUpperCase()) return name
  return name
    .replace(/([a-z0-9])([A-Z])/g, (_, a: string, b: string) => `${a} ${b.toLowerCase()}`)
    .replace(/^./, (c) => c.toUpperCase())
}

export type Tone = 'neutral' | 'positive' | 'caution' | 'negative' | 'info' | 'accent'

export function statusTone(status: string | null | undefined): Tone {
  switch (status) {
    case 'Active':
    case 'Approved':
    case 'Paid':
    case 'Lodged':
    case 'Delivered':
    case 'Accepted':
      return 'positive'
    case 'Applied':
    case 'Submitted':
    case 'UnderReview':
    case 'CoolingOff':
    case 'Pending':
    case 'Queued':
    case 'Presented':
      return 'caution'
    case 'Declined':
    case 'Rejected':
    case 'Terminated':
    case 'EarlyTerminated':
    case 'Cancelled':
    case 'CoolingOffCancelled':
    case 'Withdrawn':
    case 'Failed':
    case 'Bounced':
    case 'Expired':
      return 'negative'
    case 'Suspended':
    case 'Sent':
      return 'info'
    default:
      return 'neutral'
  }
}

// The current FBT year by its ending year (1 April to 31 March), in the browser's own day.
export function currentFbtYear(now = new Date()) {
  return now.getMonth() >= 3 ? now.getFullYear() + 1 : now.getFullYear()
}
