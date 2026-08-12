import { useState, type FormEvent } from 'react'

import {
  todoPriority,
  type TodoDraft,
} from '../types/todo.ts'

type TodoFormProps = {
  busy: boolean
  errors?: Record<string, string[]>
  initial?: TodoDraft
  legend: string
  submitLabel: string
  onCancel?: () => void
  onSubmit: (draft: TodoDraft) => Promise<boolean>
}

function today(): string {
  const current = new Date()
  const offset = current.getTimezoneOffset() * 60_000
  return new Date(current.getTime() - offset).toISOString().slice(0, 10)
}

const emptyDraft: TodoDraft = {
  name: '',
  description: '',
  dueDate: today(),
  priority: todoPriority.medium,
}

function FieldError({ messages }: { messages?: string[] }) {
  if (!messages?.length) {
    return null
  }

  return <span className="field-error">{messages.join(' ')}</span>
}

export function TodoForm({
  busy,
  errors,
  initial,
  legend,
  submitLabel,
  onCancel,
  onSubmit,
}: TodoFormProps) {
  const [draft, setDraft] = useState<TodoDraft>(initial ?? emptyDraft)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const succeeded = await onSubmit(draft)

    if (succeeded && !initial) {
      setDraft({ ...emptyDraft, dueDate: today() })
    }
  }

  return (
    <form className="todo-form" onSubmit={handleSubmit} noValidate>
      <fieldset disabled={busy}>
        <legend>{legend}</legend>

        <label>
          Name
          <input
            name="name"
            value={draft.name}
            maxLength={200}
            onChange={(event) => setDraft({ ...draft, name: event.target.value })}
          />
          <FieldError messages={errors?.name} />
        </label>

        <label>
          Description
          <textarea
            name="description"
            value={draft.description}
            maxLength={2000}
            rows={3}
            onChange={(event) => setDraft({ ...draft, description: event.target.value })}
          />
          <FieldError messages={errors?.description} />
        </label>

        <div className="form-row">
          <label>
            Due date
            <input
              name="dueDate"
              type="date"
              value={draft.dueDate}
              onChange={(event) => setDraft({ ...draft, dueDate: event.target.value })}
            />
            <FieldError messages={errors?.dueDate} />
          </label>

          <label>
            Priority
            <select
              name="priority"
              value={draft.priority}
              onChange={(event) => setDraft({
                ...draft,
                priority: Number(event.target.value) as TodoDraft['priority'],
              })}
            >
              <option value={todoPriority.low}>Low</option>
              <option value={todoPriority.medium}>Medium</option>
              <option value={todoPriority.high}>High</option>
            </select>
            <FieldError messages={errors?.priority} />
          </label>
        </div>

        <div className="form-actions">
          <button className="button primary" type="submit">
            {busy ? 'Saving…' : submitLabel}
          </button>
          {onCancel && (
            <button className="button secondary" type="button" onClick={onCancel}>
              Cancel
            </button>
          )}
        </div>
      </fieldset>
    </form>
  )
}
