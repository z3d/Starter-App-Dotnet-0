import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ShowMore } from '@/components/ui/primitives'
import { countLoaded, usePaged, withPage } from './paged'

const pages: Record<number, { data: string[]; hasMore: boolean }> = {
  1: { data: ['Ivy', 'Leo'], hasMore: true },
  2: { data: ['Zara'], hasMore: false },
}

function Names({ fetchPage }: { fetchPage: (page: number) => Promise<{ data: string[]; hasMore: boolean }> }) {
  const list = usePaged({ queryKey: ['names'], queryFn: fetchPage })
  if (list.isPending) return <p>Loading</p>
  return (
    <>
      <ul>
        {list.data?.map((name) => (
          <li key={name}>{name}</li>
        ))}
      </ul>
      <ShowMore list={list} />
    </>
  )
}

describe('a paged list', () => {
  it('shows the first page, loads the next onto the end, and says nothing more once it is whole', async () => {
    const fetchPage = vi.fn(async (page: number) => pages[page]!)
    render(
      <QueryClientProvider client={new QueryClient()}>
        <Names fetchPage={fetchPage} />
      </QueryClientProvider>,
    )

    expect(await screen.findByText('Leo')).toBeInTheDocument()
    expect(screen.queryByText('Zara')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Show more' }))

    expect(await screen.findByText('Zara')).toBeInTheDocument()
    expect(screen.getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Ivy', 'Leo', 'Zara'])
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
    expect(fetchPage.mock.calls.map(([page]) => page)).toEqual([1, 2])
  })

  it('asks for the page after any filter already on the path, and marks a count that has more to load', () => {
    expect(withPage('/api/v1/customers', 2, 100)).toBe('/api/v1/customers?page=2&pageSize=100')
    expect(withPage('/api/v1/customers?search=ava', 1, 100)).toBe('/api/v1/customers?search=ava&page=1&pageSize=100')
    expect(countLoaded(100, true)).toBe('100+')
    expect(countLoaded(7, false)).toBe('7')
  })
})
