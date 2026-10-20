// A date cut from a UTC timestamp is the UTC day, which until 10 am in Brisbane is still yesterday.
const sources = import.meta.glob<string>(
  ['/src/**/*.{ts,tsx}', '!/src/**/*.test.{ts,tsx}', '!/src/lib/api.generated.ts'],
  {
    query: '?raw',
    import: 'default',
    eager: true,
  },
)

const dayFromUtc: [RegExp, string][] = [
  [/\.(toISOString|toJSON)\(\)\s*\.(slice|substring|substr)\(\s*0\s*,\s*10\s*\)/, 'cuts the date off a UTC timestamp'],
  [/\.(toISOString|toJSON)\(\)\s*\.split\(\s*['"`]T['"`]\s*\)/, 'splits the date off a UTC timestamp'],
  [/\.getUTC(Date|Day|Month|FullYear)\(\)/, 'reads a calendar field in UTC'],
  [/timeZone:\s*['"`](UTC|Etc\/UTC)['"`]/, 'formats a date in UTC'],
]

describe('days in the web app', () => {
  it('scans the app source', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(10)
  })

  it('never takes a day from a UTC timestamp', () => {
    const failures = Object.entries(sources).flatMap(([file, text]) =>
      text
        .split('\n')
        .flatMap((line, index) =>
          dayFromUtc
            .filter(([pattern]) => pattern.test(line))
            .map(([, what]) => `${file}:${index + 1} ${what}: ${line.trim()}`),
        ),
    )

    expect(failures).toEqual([])
  })

  it('recognises the patterns it forbids', () => {
    const offenders = [
      'new Date().toISOString().slice(0, 10)',
      "d.toISOString().split('T')[0]",
      'd.getUTCDate()',
      "{ timeZone: 'UTC' }",
    ]

    for (const offender of offenders) expect(dayFromUtc.some(([pattern]) => pattern.test(offender))).toBe(true)
  })
})
