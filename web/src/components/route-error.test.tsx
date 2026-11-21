import { createMemoryHistory, createRootRoute, createRouter, RouterProvider } from '@tanstack/react-router'
import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RouteError } from './route-error'

const reportError = vi.fn()
vi.mock('@/lib/client-events', () => ({ reportError: (...args: unknown[]) => reportError(...args) }))

describe('RouteError', () => {
  afterEach(() => reportError.mockClear())

  it('names what failed, reports it, and offers a way back', async () => {
    let fail = true
    const rootRoute = createRootRoute({
      component: () => {
        if (fail) throw new Error('The order list could not be read')
        return <p>Orders</p>
      },
    })
    const router = createRouter({
      routeTree: rootRoute,
      history: createMemoryHistory({ initialEntries: ['/'] }),
      defaultErrorComponent: RouteError,
    })
    vi.spyOn(console, 'error').mockImplementation(() => {})

    render(<RouterProvider router={router} />)

    expect(await screen.findByRole('alert')).toHaveTextContent('This page could not be shown')
    expect(screen.getByText('The order list could not be read')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to the home page' })).toHaveAttribute('href', '/')
    expect(reportError).toHaveBeenCalledWith(expect.objectContaining({ message: 'The order list could not be read' }))

    fail = false
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('Orders')).toBeInTheDocument()
  })
})
