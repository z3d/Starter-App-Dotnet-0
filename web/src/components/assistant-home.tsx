import { useMutation } from '@tanstack/react-query'
import { ArrowUp, Sparkles } from 'lucide-react'
import { type FormEvent, useEffect, useMemo, useRef, useState } from 'react'
import { type Confirmed, ProposalCard } from '@/components/assistant-panel'
import { AssistantQuestion } from '@/components/assistant-question'
import { Button } from '@/components/ui/button'
import { AssistantGraphic } from '@/components/assistant-graphic'
import { AssistantTable } from '@/components/assistant-table'
import { Callout } from '@/components/ui/primitives'
import { ApiError } from '@/lib/api'
import { type AssistantArea, type AssistantMessage, type AssistantOutcome, ask, reply } from '@/lib/assistant'
import { proposalFor, useAssistantRoutes } from '@/lib/assistant-routes'
import { type Context, type Step, learn, nextSteps, starts } from '@/lib/journeys'

const openers: Record<AssistantArea, { heading: string; hint: string }> = {
  app: {
    heading: 'What do you need done?',
    hint: 'Pick what has happened, or say it in your own words. You are asked for what is needed, and nothing changes until you confirm it.',
  },
}

interface Exchange {
  said: string
  outcome?: AssistantOutcome
  failed?: string
  closed?: boolean
  setAside?: boolean
  done?: Confirmed
  // What the product offers after a confirmed step: the same steps, in the same order, every time.
  next?: Step[]
}

export function AssistantHome({
  area,
  compact = false,
  question = null,
}: {
  area: AssistantArea
  compact?: boolean
  question?: { text: string; id: number } | null
}) {
  const [thread, setThread] = useState<Exchange[]>([])
  const [text, setText] = useState('')
  const asking = useMutation({ mutationFn: (messages: AssistantMessage[]) => ask(messages, area) })
  const listed = useAssistantRoutes(area).data
  const routes = useMemo(() => listed ?? [], [listed])
  // The records this conversation has made or named, so a later step can be about them.
  const [context, setContext] = useState<Context>({})
  const end = useRef<HTMLDivElement>(null)
  const opener = openers[area]
  const last = thread[thread.length - 1]

  useEffect(() => {
    end.current?.scrollIntoView?.({ block: 'nearest' })
  }, [thread, asking.isPending])

  // One more turn of the conversation: what the person said (or nothing, when the assistant carries on) and the messages to send.
  const setAside = (exchanges: Exchange[]) =>
    exchanges.map((exchange) =>
      exchange.outcome?.proposal && !exchange.closed ? { ...exchange, closed: true, setAside: true } : exchange,
    )

  const turn = (said: string, messages: AssistantMessage[]) => {
    setThread((exchanges) => [...setAside(exchanges), { said }])
    asking.mutate(messages, {
      onSuccess: (outcome) => setThread((exchanges) => [...exchanges.slice(0, -1), { said, outcome }]),
      onError: (failure) =>
        setThread((exchanges) => [
          ...exchanges.slice(0, -1),
          {
            said,
            failed:
              failure instanceof ApiError && failure.status === 503
                ? 'The assistant is turned off here.'
                : failure.message,
          },
        ]),
    })
  }

  const say = (words: string) => {
    const said = words.trim()
    if (!said || asking.isPending) return
    // A reply to a question carries the conversation on; anything else starts a new one.
    const carriesOn = last?.outcome && !last.outcome.proposal && !last.closed
    setText('')
    turn(said, carriesOn && last?.outcome ? reply(last.outcome, said) : [{ role: 'user', text: said }])
  }

  const close = (index: number, done?: Confirmed, next?: Step[]) =>
    setThread((exchanges) =>
      exchanges.map((exchange, i) => (i === index ? { ...exchange, closed: true, done, next } : exchange)),
    )

  // A step the person picked: the product builds its card from its own list, and no model is asked.
  const take = (step: Step, said: string, known: Context = context) => {
    const proposal = proposalFor(step, routes, known)
    if (!proposal) return say(said)
    setThread((exchanges) => [...setAside(exchanges), { said, outcome: { messages: [], reply: null, proposal } }])
  }

  const begin = (start: { label: string; step?: Step }) =>
    start.step ? take(start.step, start.label, {}) : say(start.label)

  // A confirmed card stays in the conversation, with what usually follows it offered underneath.
  const confirm = (index: number, done: Confirmed) => {
    const known = done.made ? learn(context, done.made, done.made.id) : context
    setContext(known)
    close(index, done, done.made ? nextSteps(done.made, known) : [])
  }

  // The form's answers go back to the assistant by field; "nothing" to what comes next ends it here.
  const answer = (index: number, outcome: AssistantOutcome, answers: Record<string, string>, said: string) => {
    if (asking.isPending) return
    close(index)
    if (answers.next === 'nothing') return
    turn(said, reply(outcome, JSON.stringify(answers)))
  }

  const answered = useRef<number | null>(null)
  useEffect(() => {
    if (!question || answered.current === question.id) return
    answered.current = question.id
    say(question.text)
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    say(text)
  }

  const box = (
    <form onSubmit={submit} className="relative">
      <label htmlFor="assistant-ask" className="sr-only">
        Ask the assistant
      </label>
      <input
        id="assistant-ask"
        value={text}
        onChange={(event) => setText(event.target.value)}
        placeholder={thread.length === 0 ? 'Tell me what you want to do…' : 'Reply, or ask for something else…'}
        autoFocus={!compact}
        autoComplete="off"
        className="h-14 w-full rounded-2xl border border-line bg-surface-raised pr-14 pl-5 text-base text-ink shadow-pop outline-none placeholder:text-ink-tertiary focus:border-accent"
      />
      <button
        type="submit"
        aria-label="Ask"
        disabled={!text.trim() || asking.isPending}
        className="absolute top-2 right-2 grid size-10 place-items-center rounded-xl bg-accent text-accent-ink transition-opacity disabled:opacity-40"
      >
        <ArrowUp className="size-5" />
      </button>
    </form>
  )

  if (thread.length === 0)
    return (
      <div
        data-testid="assistant-home"
        className={
          compact
            ? 'flex w-full flex-col gap-5 pb-4'
            : 'mx-auto flex min-h-[70svh] w-full max-w-2xl flex-col justify-center gap-6'
        }
      >
        <div className={compact ? 'space-y-2' : 'space-y-2 text-center'}>
          {compact ? null : <Sparkles className="mx-auto size-7 text-accent" aria-hidden />}
          <h1 className={compact ? 'font-display text-2xl text-ink' : 'font-display text-4xl tracking-tight text-ink'}>
            {opener.heading}
          </h1>
          <p className="text-ink-secondary">{opener.hint}</p>
        </div>
        {box}
        <div className={compact ? 'flex flex-wrap gap-2' : 'flex flex-wrap justify-center gap-2'}>
          {starts[area].map((start) => (
            <button
              key={start.label}
              type="button"
              onClick={() => begin(start)}
              className="rounded-full border border-line bg-surface px-3 py-1.5 text-sm text-ink-secondary transition-colors hover:border-accent hover:text-ink"
            >
              {start.label}
            </button>
          ))}
        </div>
      </div>
    )

  return (
    <div
      data-testid="assistant-home"
      className={compact ? 'flex w-full flex-col gap-5 pb-4' : 'mx-auto flex w-full max-w-2xl flex-col gap-5 pb-4'}
    >
      <ol className="space-y-5" aria-label="Conversation">
        {thread.map((exchange, index) => (
          <li key={`${index}-${exchange.said}`} className="space-y-3">
            {exchange.said ? (
              <p className="ml-auto w-fit max-w-[85%] rounded-2xl rounded-br-md bg-surface-sunken px-4 py-2 text-ink">
                {exchange.said}
              </p>
            ) : null}
            {exchange.failed ? <Callout tone="negative">{exchange.failed} Nothing was changed.</Callout> : null}
            {exchange.outcome?.proposal && !exchange.closed ? (
              <ProposalCard
                proposal={exchange.outcome.proposal}
                area={area}
                onDone={() => close(index)}
                onConfirmed={(done) => confirm(index, done)}
              />
            ) : null}
            {exchange.outcome?.proposal && exchange.closed && exchange.done ? (
              <p data-testid="assistant-done" className="flex flex-wrap items-center gap-3 text-ink">
                <span>{exchange.done.said}</span>
                {exchange.done.open ? (
                  <Button type="button" variant="ghost" onClick={exchange.done.open}>
                    Open it
                  </Button>
                ) : null}
              </p>
            ) : null}
            {exchange.done && (exchange.next ?? []).length > 0 && index === thread.length - 1 ? (
              <div data-testid="assistant-next" className="space-y-2">
                <p className="text-sm text-ink-secondary">What usually comes next:</p>
                <div className="flex flex-wrap gap-2">
                  {(exchange.next ?? []).map((step) => (
                    <button
                      key={`${step.method} ${step.path} ${step.label}`}
                      type="button"
                      onClick={() => take(step, step.label)}
                      className="rounded-xl border border-line bg-surface px-4 py-2 text-left font-medium text-ink transition-colors hover:border-accent"
                    >
                      {step.label}
                    </button>
                  ))}
                </div>
              </div>
            ) : null}
            {exchange.outcome?.proposal && exchange.closed && !exchange.done ? (
              <p className="text-sm text-ink-tertiary">
                {exchange.setAside ? 'Set aside: nothing was changed.' : 'Closed. Ask for anything else below.'}
              </p>
            ) : null}
            {exchange.outcome?.table ? <AssistantTable table={exchange.outcome.table} /> : null}
            {exchange.outcome?.graphic ? <AssistantGraphic graphic={exchange.outcome.graphic} /> : null}
            {exchange.outcome && !exchange.outcome.proposal && exchange.outcome.reply ? (
              <p data-testid="assistant-reply" className="max-w-[85%] text-ink">
                {exchange.outcome.reply}
              </p>
            ) : null}
            {exchange.outcome?.question && !exchange.closed ? (
              <AssistantQuestion
                question={exchange.outcome.question}
                onAnswer={(answers, said) => exchange.outcome && answer(index, exchange.outcome, answers, said)}
              />
            ) : null}
            {exchange.outcome?.question && exchange.closed ? (
              <p className="text-ink-secondary">{exchange.outcome.question.question}</p>
            ) : null}
            {!exchange.outcome && !exchange.failed ? (
              <p role="status" className="text-ink-tertiary">
                Working on it…
              </p>
            ) : null}
          </li>
        ))}
      </ol>
      <div ref={end} />
      {/* The box follows the conversation down the page, but stays out of the way of a card being filled in. */}
      <div
        className={`space-y-2 ${last?.outcome?.proposal && !last.closed ? '' : compact ? 'sticky bottom-0 bg-canvas pt-2 pb-4' : 'sticky bottom-4'}`}
      >
        {asking.isPending ? null : (
          <button
            type="button"
            onClick={() => {
              setThread([])
              setContext({})
            }}
            className="rounded-full border border-line bg-surface px-3 py-1 text-sm text-ink-secondary transition-colors hover:border-accent hover:text-ink"
          >
            Start something new
          </button>
        )}
        {box}
      </div>
    </div>
  )
}
