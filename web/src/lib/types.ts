import type { components } from '@/lib/api.generated'

// The API's own shapes, from its OpenAPI document (`npm run gen:api`); nothing is typed by hand.
export type Schemas = components['schemas']

// A DTO's properties are all present on the wire; the document marks them optional only because
// the C# types have no `required`. `Full` says what the API actually sends.
export type Full<T> = { [K in keyof T]-?: T[K] }
