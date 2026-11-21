import { describe, expect, it } from 'vitest'
import { filled, learn, nextSteps, starts } from './journeys'

describe('the jobs the product knows', () => {
  it('starts with things that happen, and opens a card for the ones the product has a first step for', () => {
    const withSteps = starts.app.filter((start) => start.step).map((start) => [start.label, start.step!.path])

    expect(withSteps).toEqual([
      ['A new customer', '/api/v1/customers'],
      ['A new product', '/api/v1/products'],
    ])
    expect(starts.app.some((start) => !start.step)).toBe(true)
  })

  it('offers what follows a step, and learns the record it made', () => {
    const done = { method: 'POST', path: '/api/v1/customers', pathParams: {}, body: { name: 'Ada' } }

    expect(nextSteps(done, {}).map((step) => step.label)).toEqual(['Add another customer'])
    expect(learn({}, done, '41')).toEqual({ customerId: '41' })
    expect(nextSteps({ method: 'DELETE', path: '/api/v1/customers/{id}' }, {})).toEqual([])
  })

  it('fills a step from what the conversation knows, and leaves out what it does not', () => {
    const step = {
      label: 'Change the customer',
      method: 'PUT' as const,
      path: '/api/v1/customers/{id}',
      params: { id: 'customerId' },
      body: { id: 'customerId', email: 'emailOnFile' },
    }

    expect(filled(step, { customerId: '41' })).toEqual({ pathParams: { id: '41' }, body: { id: '41' } })
  })
})
