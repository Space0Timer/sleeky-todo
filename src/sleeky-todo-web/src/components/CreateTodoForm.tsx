import { useState, type FormEvent } from 'react'

import {
  recurrenceType,
  recurrenceUnit,
  todoPriority,
  type CreateTodoDraft,
  type RecurrenceType,
  type RecurrenceUnit,
} from '../types/todo.ts'

type CreateTodoFormProps = {
  busy: boolean
  errors?: Record<string, string[]>
  onSubmit: (draft: CreateTodoDraft) => Promise<boolean>
}

function today(): string {
  const current = new Date()
  const offset = current.getTimezoneOffset() * 60_000
  return new Date(current.getTime() - offset).toISOString().slice(0, 10)
}

function emptyDraft(): CreateTodoDraft {
  return {
    name: '',
    description: '',
    dueDate: today(),
    priority: todoPriority.medium,
    recurrence: null,
  }
}

function FieldError({ messages }: { messages?: string[] }) {
  return messages?.length
    ? <span className="field-error">{messages.join(' ')}</span>
    : null
}

export function CreateTodoForm({ busy, errors, onSubmit }: CreateTodoFormProps) {
  const [draft, setDraft] = useState<CreateTodoDraft>(emptyDraft)
  const recurring = draft.recurrence !== null

  function setRecurring(enabled: boolean) {
    setDraft({
      ...draft,
      recurrence: enabled
        ? { type: recurrenceType.daily, interval: 1, unit: null }
        : null,
    })
  }

  function setRecurrenceType(type: RecurrenceType) {
    setDraft({
      ...draft,
      recurrence: {
        type,
        interval: type === recurrenceType.custom ? 2 : 1,
        unit: type === recurrenceType.custom ? recurrenceUnit.days : null,
      },
    })
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (await onSubmit(draft)) setDraft(emptyDraft())
  }

  return (
    <form className="todo-form" onSubmit={handleSubmit} noValidate>
      <fieldset disabled={busy}>
        <legend>Create a TODO</legend>

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
                priority: Number(event.target.value) as CreateTodoDraft['priority'],
              })}
            >
              <option value={todoPriority.low}>Low</option>
              <option value={todoPriority.medium}>Medium</option>
              <option value={todoPriority.high}>High</option>
            </select>
            <FieldError messages={errors?.priority} />
          </label>
        </div>

        <label className="checkbox-label">
          <input
            checked={recurring}
            type="checkbox"
            onChange={(event) => setRecurring(event.target.checked)}
          />
          Repeat this TODO
        </label>

        {draft.recurrence && (
          <div className="recurrence-fields" aria-label="Recurrence schedule">
            <label>
              Frequency
              <select
                aria-label="Recurrence frequency"
                value={draft.recurrence.type}
                onChange={(event) => setRecurrenceType(
                  Number(event.target.value) as RecurrenceType,
                )}
              >
                <option value={recurrenceType.daily}>Daily</option>
                <option value={recurrenceType.weekly}>Weekly</option>
                <option value={recurrenceType.monthly}>Monthly</option>
                <option value={recurrenceType.custom}>Custom</option>
              </select>
            </label>

            {draft.recurrence.type === recurrenceType.custom && (
              <>
                <label>
                  Every
                  <input
                    aria-label="Recurrence interval"
                    min={1}
                    type="number"
                    value={draft.recurrence.interval}
                    onChange={(event) => setDraft({
                      ...draft,
                      recurrence: {
                        ...draft.recurrence!,
                        interval: Number(event.target.value),
                      },
                    })}
                  />
                  <FieldError messages={errors?.recurrenceInterval} />
                </label>
                <label>
                  Unit
                  <select
                    aria-label="Recurrence unit"
                    value={draft.recurrence.unit ?? recurrenceUnit.days}
                    onChange={(event) => setDraft({
                      ...draft,
                      recurrence: {
                        ...draft.recurrence!,
                        unit: Number(event.target.value) as RecurrenceUnit,
                      },
                    })}
                  >
                    <option value={recurrenceUnit.days}>Days</option>
                    <option value={recurrenceUnit.weeks}>Weeks</option>
                    <option value={recurrenceUnit.months}>Months</option>
                  </select>
                  <FieldError messages={errors?.recurrenceUnit} />
                </label>
              </>
            )}
          </div>
        )}

        <div className="form-actions">
          <button className="button primary" type="submit">
            {busy ? 'Saving…' : 'Add TODO'}
          </button>
        </div>
      </fieldset>
    </form>
  )
}
