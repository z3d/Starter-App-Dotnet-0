import { useQuery } from '@tanstack/react-query'
import type { RouteField } from '@/lib/assistant'
import { list } from '@/lib/assistant-routes'

const control =
  'h-10 w-full rounded-md border border-line bg-surface-raised px-3 text-ink outline-none focus:border-accent disabled:opacity-60'
// A pick-list drawn like the product's own forms, not the browser's: one chevron, the same padding.
const picker = `${control} select-chevron`

// A field's name as a person reads it: "firstName" is "First name", "customerId" is "Customer".
export const words = (name: string) => {
  const spaced = name
    .replace(/Id$/, '')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

// A value the API takes, as a person reads it: "InProgress" is "In progress".
export const plain = (value: string) => (/^[A-Z]{2,3}$/.test(value) ? value : words(value))

type Values = Record<string, unknown>

// Which records a field picks from, by its name; the list comes from the same API the rest of the product reads.
function source(
  field: string,
): { path: string; params?: Record<string, string>; label: (record: Record<string, unknown>) => string } | null {
  const text = (value: unknown) => (typeof value === 'string' || typeof value === 'number' ? String(value) : '')
  if (field === 'customerId') return { path: '/api/v1/customers', label: (record) => text(record.name) }
  if (field === 'productId') return { path: '/api/v1/products', label: (record) => text(record.name) }
  return null
}

function RecordPicker({
  id,
  from,
  value,
  onChange,
}: {
  id: string
  from: NonNullable<ReturnType<typeof source>>
  value: string
  onChange: (value: string) => void
}) {
  const records = useQuery({
    queryKey: ['assistant', 'pick', from.path, from.params ?? {}],
    queryFn: () => list(from.path, from.params),
    staleTime: 15_000,
  })
  return (
    <select id={id} value={value} onChange={(event) => onChange(event.target.value)} className={picker}>
      <option value="">{records.isPending ? 'Loading…' : 'Choose…'}</option>
      {(records.data ?? []).map((record) => (
        <option key={String(record.id)} value={String(record.id)}>
          {from.label(record)}
        </option>
      ))}
    </select>
  )
}

// The body of a change, as a form: every field the route takes, in the route's own order, the same every time.
// What was already said or known arrives filled in and stays editable; nothing is sent until the person confirms.
export function RouteFields({
  fields,
  values,
  onChange,
}: {
  fields: RouteField[]
  values: Values
  onChange: (name: string, value: unknown) => void
}) {
  return (
    <div className="space-y-3">
      {fields
        .filter((field) => field.kind !== 'array' && field.kind !== 'object' && applies(field, values))
        .map((field) => {
          const name = field.name ?? ''
          const id = `field-${name}`
          const value = values[name]
          const text = value == null ? '' : String(value)
          const from = field.kind === 'uuid' || field.kind === 'integer' ? source(name) : null
          return (
            <div key={name} className="space-y-1">
              <label htmlFor={id} className="block text-sm font-medium text-ink-secondary">
                {field.label ?? words(name)}
                {needed(field) ? '' : ' (optional)'}
              </label>
              {field.kind === 'choice' ? (
                <select
                  id={id}
                  value={text}
                  onChange={(event) => onChange(name, event.target.value)}
                  className={picker}
                >
                  <option value="">Choose…</option>
                  {(field.options ?? []).map((option) => (
                    <option key={option} value={option}>
                      {plain(option)}
                    </option>
                  ))}
                </select>
              ) : field.kind === 'boolean' ? (
                <select
                  id={id}
                  value={value === true ? 'yes' : value === false ? 'no' : ''}
                  onChange={(event) =>
                    onChange(name, event.target.value === '' ? undefined : event.target.value === 'yes')
                  }
                  className={picker}
                >
                  <option value="">Choose…</option>
                  <option value="yes">Yes</option>
                  <option value="no">No</option>
                </select>
              ) : from ? (
                <RecordPicker
                  id={id}
                  from={from}
                  value={text}
                  onChange={(picked) =>
                    onChange(name, field.kind === 'integer' && picked !== '' ? Number(picked) : picked)
                  }
                />
              ) : (
                <input
                  id={id}
                  type={
                    field.kind === 'date'
                      ? 'date'
                      : field.kind === 'number' || field.kind === 'integer'
                        ? 'number'
                        : 'text'
                  }
                  step={field.kind === 'number' ? '0.01' : field.kind === 'integer' ? '1' : undefined}
                  value={text}
                  onChange={(event) =>
                    onChange(
                      name,
                      field.kind === 'number' || field.kind === 'integer'
                        ? event.target.value === ''
                          ? undefined
                          : Number(event.target.value)
                        : event.target.value,
                    )
                  }
                  className={control}
                />
              )}
            </div>
          )
        })}
    </div>
  )
}

// A field that applies only for some values of another is asked for once that other field holds one of them, and is
// needed then.
export function applies(field: RouteField, values: Values) {
  if (!field.whenField) return true
  const chosen = values[field.whenField]
  return typeof chosen === 'string' && (field.whenValues ?? []).includes(chosen)
}

const needed = (field: RouteField) => field.required === true || Boolean(field.whenField)

// A needed field with nothing in it; Confirm waits on these.
export function missing(fields: RouteField[], values: Values) {
  return fields.filter((field) => {
    const value = values[field.name ?? '']
    return (
      needed(field) &&
      applies(field, values) &&
      field.kind !== 'array' &&
      field.kind !== 'object' &&
      (value == null || value === '')
    )
  })
}

// What is sent: every value given, less any for a field that no longer applies.
export function sendable(fields: RouteField[], values: Values) {
  const dropped = new Set(fields.filter((field) => !applies(field, values)).map((field) => field.name ?? ''))
  return Object.fromEntries(
    Object.entries(values).filter(([name, value]) => value !== undefined && value !== '' && !dropped.has(name)),
  )
}
