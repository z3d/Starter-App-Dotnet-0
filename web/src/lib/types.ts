import type { components } from '@/lib/api.generated'

// The API's own shapes, from its OpenAPI document (`npm run gen:api`); nothing is typed by hand.
export type Schemas = components['schemas']

// A DTO's properties are all present on the wire; the document marks them optional only because
// the C# types have no `required`, and types a number as `number | string` only because the API
// also reads one from a string (JsonSerializerDefaults.Web). `Full` says what the API actually
// sends, nested lists and objects included, nullable or not.
export type Full<T> = [T] extends [null] ? T : null extends T ? FullValue<Exclude<T, null>> | null : FullValue<T>

type FullValue<T> = [T] extends [(infer U)[]]
  ? Full<U>[]
  : [T] extends [object]
    ? { [K in keyof T]-?: Full<Exclude<T[K], undefined>> }
    : [number] extends [Extract<T, number>]
      ? Exclude<T, string>
      : T
