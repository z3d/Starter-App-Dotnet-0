import { useQuery } from '@tanstack/react-query'
import { call, client } from '@/lib/api'
import type { AssistantArea, AssistantRoute, Proposal } from '@/lib/assistant'
import { type Context, type Step, filled } from '@/lib/journeys'

// The changes a person in this area may make, each with the fields its card asks for.
export function useAssistantRoutes(area: AssistantArea) {
  return useQuery({
    queryKey: ['assistant', 'routes', area],
    queryFn: () => call(client.GET('/api/v1/assistant/routes', { params: { query: { area } } })),
    staleTime: 30 * 60_000,
    select: (data) => data.routes ?? [],
  })
}

// The card for a step the person picked: built by the product from its own list, with no model involved.
export function proposalFor(
  step: Step,
  routes: AssistantRoute[],
  context: Context,
): Extract<Proposal, { kind: 'request' }> | null {
  const route = routes.find((candidate) => candidate.method === step.method && candidate.path === step.path)
  if (!route) return null
  const { pathParams, body } = filled(step, context)
  return {
    kind: 'request',
    title: step.label,
    method: step.method,
    path: step.path,
    summary: route.summary ?? '',
    scope: route.scope ?? '',
    pathParams,
    body,
    fields: route.fields ?? [],
  }
}

// A list read by its path, for a pick-list; the path is one the product names, not one the generated types know.
export async function list(path: string, pathParams: Record<string, string> = {}): Promise<Record<string, unknown>[]> {
  const read = client.GET as unknown as (
    path: string,
    init: object,
  ) => Promise<{ data?: unknown; error?: unknown; response: Response }>
  // Most lists come a page at a time; a short one comes whole.
  const found = await call(read(path, { params: { path: pathParams, query: { page: 1, pageSize: 50 } } }))
  if (Array.isArray(found)) return found as Record<string, unknown>[]
  return (found as { data?: Record<string, unknown>[] } | null)?.data ?? []
}
