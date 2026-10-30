import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Button } from './button'
import { Checkbox, Field } from './field'
import { SplitBar } from './figures'
import { Avatar, Callout, Chip, EmptyState, PageHeader, StatTile, Steps } from './primitives'
import { Queue, QueueRow, StageFlow, Waited } from './queue'

// The component vocabulary (docs/DESIGN.md): every screen is built from these.
describe('PageHeader', () => {
  it("is the page's one heading, with its eyebrow, description and actions", () => {
    render(
      <PageHeader eyebrow="Wilston" title="Properties" description="Every listing." actions={<Button>Add</Button>} />,
    )
    expect(screen.getByRole('heading', { level: 1, name: 'Properties' })).toBeInTheDocument()
    expect(screen.getByText('Wilston')).toBeInTheDocument()
    expect(screen.getByText('Every listing.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument()
  })
})

describe('Field', () => {
  it('labels its control and shows the error in place of the hint', () => {
    const { rerender } = render(
      <Field label="Email" hint="Where the consent goes">
        <input name="email" />
      </Field>,
    )
    expect(screen.getByLabelText(/^Email/)).toHaveAttribute('name', 'email')
    expect(screen.getByText('Where the consent goes')).toBeInTheDocument()
    rerender(
      <Field label="Email" hint="Where the consent goes" error="Not an email address">
        <input name="email" />
      </Field>,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('Not an email address')
    expect(screen.queryByText('Where the consent goes')).not.toBeInTheDocument()
  })

  it('a checkbox carries its label', async () => {
    const onChange = vi.fn()
    render(<Checkbox label="May text" name="consentSms" onChange={onChange} />)
    await userEvent.click(screen.getByLabelText('May text'))
    expect(onChange).toHaveBeenCalled()
  })
})

describe('Chip and Callout', () => {
  it('speak the tones', () => {
    render(
      <>
        <Chip tone="positive">Matched</Chip>
        <Chip tone="negative">Deal-breaker</Chip>
        <Callout tone="caution">Check the consent</Callout>
      </>,
    )
    expect(screen.getByText('Matched')).toHaveClass('text-positive')
    expect(screen.getByText('Deal-breaker')).toHaveClass('text-negative')
    expect(screen.getByText('Check the consent').parentElement).toHaveClass('text-caution')
  })
})

describe('StatTile and EmptyState', () => {
  it('render the number and the empty message', () => {
    render(
      <>
        <StatTile label="Open briefs" value="12" hint="3 new this week" />
        <EmptyState title="No properties yet" description="Add the first appraisal." />
      </>,
    )
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('Open briefs')).toBeInTheDocument()
    expect(screen.getByText('No properties yet')).toBeInTheDocument()
    expect(screen.getByText('Add the first appraisal.')).toBeInTheDocument()
  })
})

describe('SplitBar', () => {
  it('names every part with its amount and leaves out a part that is nothing', () => {
    render(
      <SplitBar
        label="The order total"
        segments={[
          { label: 'Excluding GST', value: 300, display: '$300.00', tone: 'accent' },
          { label: 'GST', value: 30, display: '$30.00', tone: 'info' },
          { label: 'Discount', value: 0, display: '$0.00', tone: 'rest' },
        ]}
      />,
    )
    expect(screen.getByRole('figure', { name: 'The order total' })).toBeInTheDocument()
    expect(screen.getByText('Excluding GST').nextElementSibling).toHaveTextContent('$300.00')
    expect(screen.getByText('GST').nextElementSibling).toHaveTextContent('$30.00')
    expect(screen.queryByText('Discount')).not.toBeInTheDocument()
  })
})

describe('Steps and Avatar', () => {
  it('marks the current step, names a stopped one and draws initials', () => {
    render(
      <>
        <Steps
          label="Order stage"
          steps={[
            { label: 'Placed', state: 'done' },
            { label: 'Confirmed', state: 'current' },
            { label: 'Cancelled', state: 'stopped' },
          ]}
        />
        <Avatar name="Ada Lovelace" />
      </>,
    )
    expect(screen.getByRole('list', { name: 'Order stage' })).toBeInTheDocument()
    expect(screen.getByText('Confirmed').closest('li')).toHaveAttribute('aria-current', 'step')
    expect(screen.getByText('Cancelled')).toHaveClass('text-negative')
    expect(screen.getByText('AL')).toBeInTheDocument()
  })
})

describe('Button', () => {
  it('is disabled while busy and still names itself', () => {
    render(<Button disabled>Save contact</Button>)
    expect(screen.getByRole('button', { name: 'Save contact' })).toBeDisabled()
  })
})

describe('StageFlow', () => {
  it('selects a stage, and clears it when the selected stage is pressed again', async () => {
    const onSelect = vi.fn()
    const stages = [
      { key: 'Applied', label: 'Applied', count: 3, urgent: true },
      { key: 'Active', label: 'Active', count: 12 },
    ]
    const { rerender } = render(<StageFlow label="Leases by stage" stages={stages} onSelect={onSelect} />)
    await userEvent.click(screen.getByRole('button', { name: /Applied/ }))
    expect(onSelect).toHaveBeenLastCalledWith('Applied')
    rerender(<StageFlow label="Leases by stage" stages={stages} selected="Applied" onSelect={onSelect} />)
    expect(screen.getByRole('button', { name: /Applied/ })).toHaveAttribute('aria-pressed', 'true')
    await userEvent.click(screen.getByRole('button', { name: /Applied/ }))
    expect(onSelect).toHaveBeenLastCalledWith(undefined)
  })
})

describe('QueueRow', () => {
  it('shows who, the next step and how long it has waited', () => {
    render(
      <Queue>
        <QueueRow
          title="Ada Lovelace"
          next="Start the review"
          reason="No receipt attached yet"
          waited={<Waited days={1} tone="slow" />}
        />
      </Queue>,
    )
    const row = screen.getByRole('listitem')
    expect(row).toHaveTextContent('Ada Lovelace')
    expect(row).toHaveTextContent('Start the reviewNo receipt attached yet')
    expect(row).toHaveTextContent('Waiting 1 day')
  })

  it('says under a day for something that arrived today', () => {
    render(<Waited days={0} />)
    expect(screen.getByText(/Waiting under a day/)).toBeInTheDocument()
  })
})
