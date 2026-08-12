import { useState } from 'react'

import type { Todo, TodoDraft } from '../types/todo.ts'
import { TodoForm } from './TodoForm.tsx'

type TodoCardProps = {
  busy: boolean
  errors?: Record<string, string[]>
  todo: Todo
  onDelete: (todo: Todo) => Promise<void>
  onRestore: (todo: Todo) => Promise<void>
  onUpdate: (todo: Todo, draft: TodoDraft) => Promise<boolean>
}

const priorityLabels = ['Low', 'Medium', 'High']

export function TodoCard({
  busy,
  errors,
  todo,
  onDelete,
  onRestore,
  onUpdate,
}: TodoCardProps) {
  const [editing, setEditing] = useState(false)
  const deleted = todo.deletedAt !== null

  async function handleUpdate(draft: TodoDraft): Promise<boolean> {
    const succeeded = await onUpdate(todo, draft)
    if (succeeded) {
      setEditing(false)
    }

    return succeeded
  }

  if (editing) {
    return (
      <article className="todo-card" data-testid={`todo-${todo.id}`}>
        <TodoForm
          busy={busy}
          errors={errors}
          initial={{
            name: todo.name,
            description: todo.description ?? '',
            dueDate: todo.dueDate,
            priority: todo.priority,
          }}
          legend={`Edit ${todo.name}`}
          submitLabel="Save changes"
          onCancel={() => setEditing(false)}
          onSubmit={handleUpdate}
        />
      </article>
    )
  }

  return (
    <article className="todo-card" data-testid={`todo-${todo.id}`}>
      <div className="todo-card-heading">
        <div>
          <span className={`priority priority-${todo.priority}`}>
            {priorityLabels[todo.priority]}
          </span>
          <h3>{todo.name}</h3>
        </div>
        <span className="version">v{todo.version}</span>
      </div>

      {todo.description && <p>{todo.description}</p>}
      <dl>
        <div>
          <dt>Due</dt>
          <dd>{todo.dueDate}</dd>
        </div>
        <div>
          <dt>ID</dt>
          <dd className="todo-id">{todo.id}</dd>
        </div>
      </dl>

      <div className="card-actions">
        {deleted ? (
          <button
            className="button primary"
            disabled={busy}
            type="button"
            onClick={() => void onRestore(todo)}
          >
            Restore
          </button>
        ) : (
          <>
            <button
              className="button secondary"
              disabled={busy}
              type="button"
              onClick={() => setEditing(true)}
            >
              Edit
            </button>
            <button
              className="button danger"
              disabled={busy}
              type="button"
              onClick={() => void onDelete(todo)}
            >
              Delete
            </button>
          </>
        )}
      </div>
    </article>
  )
}
