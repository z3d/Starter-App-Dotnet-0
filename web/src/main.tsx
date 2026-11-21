import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRouter, RouterProvider } from '@tanstack/react-router'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AuthProvider, useAuth } from 'react-oidc-context'
import { Toaster } from 'sonner'
import { RouteError } from '@/components/route-error'
import { oidcSettings, onSigninCallback } from '@/lib/auth'
import { installErrorReporting } from '@/lib/client-events'
import { routeTree } from './routeTree.gen'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // The API client already tries a failed read once more before it reports an outage.
      retry: false,
    },
  },
})

const router = createRouter({
  routeTree,
  context: { auth: undefined!, queryClient },
  defaultPreload: 'intent',
  defaultErrorComponent: RouteError,
  scrollRestoration: true,
})

installErrorReporting()

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

function App() {
  const auth = useAuth()
  return <RouterProvider router={router} context={{ auth, queryClient }} />
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AuthProvider {...oidcSettings} onSigninCallback={onSigninCallback}>
      <QueryClientProvider client={queryClient}>
        <App />
        <Toaster position="bottom-right" mobileOffset={{ bottom: 88 }} richColors closeButton />
      </QueryClientProvider>
    </AuthProvider>
  </StrictMode>,
)
