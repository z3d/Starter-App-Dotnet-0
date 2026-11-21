import { fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { RouteField } from '@/lib/assistant'
import { RouteFields, applies, missing, plain, sendable, words } from './assistant-form'

vi.mock('@/lib/assistant-routes', () => ({
  list: (path: string) =>
    Promise.resolve(
      path === '/api/v1/customers'
        ? [
            { id: 7, name: 'Ada Lovelace' },
            { id: 9, name: 'Grace Hopper' },
          ]
        : [],
    ),
}))

const fields: RouteField[] = [
  { name: 'name', required: true, kind: 'text', options: [] },
  { name: 'customerId', required: false, kind: 'integer', options: [] },
  { name: 'price', required: true, kind: 'number', options: [] },
  { name: 'status', required: true, kind: 'choice', options: ['Pending', 'InProgress'] },
  { name: 'shipBy', required: false, kind: 'date', options: [] },
  { name: 'isActive', required: true, kind: 'boolean', options: [] },
]

function Form({ start }: { start: Record<string, unknown> }) {
  const [values, setValues] = useState(start)
  return (
    <QueryClientProvider client={new QueryClient()}>
      <RouteFields
        fields={fields}
        values={values}
        onChange={(name, value) => setValues((current) => ({ ...current, [name]: value }))}
      />
      <output data-testid="values">{JSON.stringify(values)}</output>
      <output data-testid="missing">
        {missing(fields, values)
          .map((field) => field.name)
          .join(',')}
      </output>
    </QueryClientProvider>
  )
}

describe('the form a change is asked for with', () => {
  it('shows every field of the route in its order, filled with what is known, and says which required ones are waiting', () => {
    render(<Form start={{ name: 'Widget', status: 'InProgress' }} />)

    expect(screen.getAllByText(/./, { selector: 'label' }).map((label) => label.textContent)).toEqual([
      'Name',
      'Customer (optional)',
      'Price',
      'Status',
      'Ship by (optional)',
      'Is active',
    ])
    expect(screen.getByLabelText('Name')).toHaveValue('Widget')
    expect(screen.getByLabelText('Status')).toHaveValue('InProgress')
    expect(screen.getByTestId('missing')).toHaveTextContent('price,isActive')
  })

  it('takes a number as a number, a yes or no as true or false, and a record as its id picked by name', async () => {
    render(<Form start={{}} />)

    fireEvent.change(screen.getByLabelText('Price'), { target: { value: '12.5' } })
    fireEvent.change(screen.getByLabelText('Is active'), { target: { value: 'no' } })
    await screen.findByRole('option', { name: 'Ada Lovelace' })
    fireEvent.change(screen.getByLabelText('Customer (optional)'), { target: { value: '7' } })

    expect(JSON.parse(screen.getByTestId('values').textContent ?? '{}')).toEqual({
      price: 12.5,
      isActive: false,
      customerId: 7,
    })
  })

  it('reads field names and values the way a person does', () => {
    expect([words('firstName'), words('customerId'), words('isActive'), plain('InProgress'), plain('USD')]).toEqual([
      'First name',
      'Customer',
      'Is active',
      'In progress',
      'USD',
    ])
  })
})

describe('a field that applies only for some values of another', () => {
  const delivery: RouteField[] = [
    { name: 'method', required: true, kind: 'choice', options: ['Courier', 'Pickup'] },
    {
      name: 'address',
      required: false,
      kind: 'text',
      options: [],
      label: 'Where it goes',
      whenField: 'method',
      whenValues: ['Courier'],
    },
    {
      name: 'pickupDate',
      required: false,
      kind: 'date',
      options: [],
      label: 'Day it is collected',
      whenField: 'method',
      whenValues: ['Pickup'],
    },
  ]
  const [, address, pickupDate] = delivery as [RouteField, RouteField, RouteField]

  it('is not asked for until the other field is chosen, then only the one that fits, and it is needed', () => {
    expect(applies(address, {})).toBe(false)
    expect(missing(delivery, {}).map((field) => field.name)).toEqual(['method'])
    expect(applies(address, { method: 'Courier' })).toBe(true)
    expect(applies(pickupDate, { method: 'Courier' })).toBe(false)
    expect(missing(delivery, { method: 'Courier' }).map((field) => field.name)).toEqual(['address'])
    expect(missing(delivery, { method: 'Pickup', pickupDate: '2026-12-01' })).toEqual([])
  })

  it('is left out of what is sent once it no longer applies', () => {
    expect(sendable(delivery, { method: 'Pickup', address: '1 Main St', pickupDate: '2026-12-01' })).toEqual({
      method: 'Pickup',
      pickupDate: '2026-12-01',
    })
  })
})
