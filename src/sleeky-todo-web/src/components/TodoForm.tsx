import { useState, type FormEvent } from 'react'

import {
  todoPriority,
  type TodoDraft,
} from '../types/todo.ts'
import styles from './common/Form.module.scss'
import { Button, FieldError } from './common/index.ts'

type TodoFormProps = {
  busy: boolean
  errors?: Record<string, string[]>
  initial?: TodoDraft
  legend: string
  submitLabel: string
  onCancel?: () => void
  onSubmit: (draft: TodoDraft) => Promise<boolean>
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
  const [draft, setDraft] = useState<TodoDraft>(initial ?? {
    name: '',
    description: '',
    dueDate: '',
    priority: todoPriority.medium,
  })

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await onSubmit(draft)
  }

  return (
    <form className={styles.todoForm} onSubmit={handleSubmit} noValidate>
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

        <div className={styles.formRow}>
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

        <div className={styles.formActions}>
          <Button variant="primary" type="submit">
            {busy ? 'Saving…' : submitLabel}
          </Button>
          {onCancel && (
            <Button variant="secondary" onClick={onCancel}>
              Cancel
            </Button>
          )}
        </div>
      </fieldset>
    </form>
  )
}
