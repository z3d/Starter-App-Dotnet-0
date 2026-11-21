import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { markStale, reportOutage, resetConnectionForTests, setServiceNotice } from '@/lib/connection'
import { ConnectionBanner } from './connection-banner'

describe('ConnectionBanner', () => {
  afterEach(() => resetConnectionForTests())

  it('shows nothing while all is well', () => {
    render(<ConnectionBanner />)
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('offers a reload to a stale build, and says what is wrong with the service', () => {
    render(<ConnectionBanner />)
    act(() => {
      markStale()
      reportOutage('offline')
      setServiceNotice('Payments are slow today')
    })
    const banner = screen.getByRole('status')
    expect(banner).toHaveTextContent('A newer version of this app is available.')
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
    expect(banner).toHaveTextContent('You appear to be offline.')
    expect(banner).toHaveTextContent('Payments are slow today')
  })
})
