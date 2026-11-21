import { type FormEvent, useState } from 'react'
import { Button } from '@/components/ui/button'
import type { AssistantQuestion as Question, QuestionField } from '@/lib/assistant'

const control =
  'h-10 w-full rounded-md border border-line bg-surface-raised px-3 text-ink outline-none focus:border-accent'

function shown(field: QuestionField, value: string) {
  if (field.kind === 'choice') return field.options.find((option) => option.value === value)?.label ?? value
  if (field.kind === 'yesno') return value === 'yes' ? 'Yes' : 'No'
  if (field.kind === 'amount') return `$${value}`
  return value
}

export function AssistantQuestion({
  question,
  onAnswer,
}: {
  question: Question
  onAnswer: (answers: Record<string, string>, said: string) => void
}) {
  const [values, setValues] = useState<Record<string, string>>({})
  const [only] = question.fields
  const quick = question.fields.length === 1 && only?.kind === 'choice'
  const set = (name: string, value: string) => setValues((current) => ({ ...current, [name]: value }))
  const missing = question.fields.filter((field) => !field.optional && !values[field.name]?.trim())

  const answer = (answers: Record<string, string>) => {
    const given = question.fields.filter((field) => answers[field.name]?.trim())
    const said =
      given.length === 1 && given[0]
        ? shown(given[0], answers[given[0].name] ?? '')
        : given.map((field) => `${field.label}: ${shown(field, answers[field.name] ?? '')}`).join(' · ')
    onAnswer(answers, said || 'Nothing to add')
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (missing.length === 0) answer(values)
  }

  if (quick && only)
    return (
      <section data-testid="assistant-question" aria-label={question.question} className="space-y-3">
        <p className="text-ink">{question.question}</p>
        <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
          {only.options.map((option) => (
            <button
              key={option.value}
              type="button"
              onClick={() => answer({ [only.name]: option.value })}
              className="rounded-xl border border-line bg-surface px-4 py-2.5 text-left transition-colors hover:border-accent"
            >
              <span className="block font-medium text-ink">{option.label}</span>
              {option.hint ? <span className="block text-sm text-ink-secondary">{option.hint}</span> : null}
            </button>
          ))}
        </div>
      </section>
    )

  return (
    <section data-testid="assistant-question" aria-label={question.question}>
      <form onSubmit={submit} className="space-y-4 rounded-lg border border-line bg-surface p-4">
        <p className="text-ink">{question.question}</p>
        {question.fields.map((field) => (
          <div key={field.name} className="space-y-1.5">
            {field.kind === 'choice' || field.kind === 'yesno' ? (
              <fieldset className="space-y-1.5">
                <legend className="text-sm font-medium text-ink-secondary">
                  {field.label}
                  {field.optional ? ' (optional)' : ''}
                </legend>
                <div className="flex flex-wrap gap-2">
                  {(field.kind === 'yesno'
                    ? [
                        { value: 'yes', label: 'Yes', hint: null },
                        { value: 'no', label: 'No', hint: null },
                      ]
                    : field.options
                  ).map((option) => (
                    <label
                      key={option.value}
                      className={`cursor-pointer rounded-lg border px-3 py-2 text-sm transition-colors ${values[field.name] === option.value ? 'border-accent bg-accent-soft text-ink' : 'border-line bg-surface-raised text-ink-secondary hover:border-accent'}`}
                    >
                      <input
                        type="radio"
                        name={field.name}
                        value={option.value}
                        checked={values[field.name] === option.value}
                        onChange={() => set(field.name, option.value)}
                        className="sr-only"
                      />
                      <span className="font-medium text-ink">{option.label}</span>
                      {option.hint ? <span className="block text-ink-secondary">{option.hint}</span> : null}
                    </label>
                  ))}
                </div>
              </fieldset>
            ) : (
              <>
                <label htmlFor={`ask-${field.name}`} className="text-sm font-medium text-ink-secondary">
                  {field.label}
                  {field.optional ? ' (optional)' : ''}
                </label>
                <input
                  id={`ask-${field.name}`}
                  type={field.kind === 'date' ? 'date' : field.kind === 'amount' ? 'number' : 'text'}
                  inputMode={field.kind === 'amount' ? 'decimal' : undefined}
                  step={field.kind === 'amount' ? '0.01' : undefined}
                  min={field.kind === 'amount' ? '0' : undefined}
                  value={values[field.name] ?? ''}
                  onChange={(event) => set(field.name, event.target.value)}
                  className={control}
                />
              </>
            )}
          </div>
        ))}
        <Button type="submit" variant="accent" disabled={missing.length > 0}>
          Continue
        </Button>
      </form>
    </section>
  )
}
