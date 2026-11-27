import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createRouter, RouterProvider } from '@tanstack/react-router'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { Toaster } from 'sonner'
import { RouteError } from '@/components/route-error'
import { SessionProvider, useSession } from '@/lib/session'
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
  const auth = useSession()
  return <RouterProvider router={router} context={{ auth, queryClient }} />
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SessionProvider>
      <QueryClientProvider client={queryClient}>
        <App />
        <Toaster position="bottom-right" mobileOffset={{ bottom: 88 }} richColors closeButton />
      </QueryClientProvider>
    </SessionProvider>
  </StrictMode>,
)
