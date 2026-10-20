import type { QueryClient } from '@tanstack/react-query'
import { createRootRouteWithContext, Outlet } from '@tanstack/react-router'
import type { AuthContextProps } from 'react-oidc-context'

export interface RouterContext {
  auth: AuthContextProps
  queryClient: QueryClient
}

export const Route = createRootRouteWithContext<RouterContext>()({
  component: () => <Outlet />,
})
