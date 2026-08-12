import { useEffect, useMemo, useState } from 'react'

import {
  todoPriorityLabels,
  todoScope,
  todoStatus,
  todoStatusLabels,
  type Todo,
  type TodoDraft,
  type TodoListItem,
  type TodoScope,
  type TodoStatus,
} from '../types/todo.ts'
import { TodoForm } from './TodoForm.tsx'

type TodoCardProps = {
  busy: boolean
  candidates: TodoListItem[]
  errors?: Record<string, string[]>
  item: TodoListItem
  scope: TodoScope
  onAddDependency: (todo: Todo, dependencyId: string) => Promise<Todo | null>
  onDelete: (todo: TodoListItem) => Promise<boolean>
  onLoad: (id: string, quiet?: boolean) => Promise<Todo | null>
  onRemoveDependency: (todo: Todo, dependencyId: string) => Promise<Todo | null>
  onRestore: (todo: TodoListItem) => Promise<boolean>
  onStatus: (todo: Todo, status: TodoStatus) => Promise<Todo | null>
  onUpdate: (todo: Todo, draft: TodoDraft) => Promise<Todo | null>
}

function formatDateTime(value: string | null): string {
  if (!value) return '—'
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

export function TodoCard({
  busy,
  candidates,
  errors,
  item,
  scope,
  onAddDependency,
  onDelete,
  onLoad,
  onRemoveDependency,
  onRestore,
  onStatus,
  onUpdate,
}: TodoCardProps) {
  const [details, setDetails] = useState<Todo | null>(null)
  const [dependencyNames, setDependencyNames] = useState<Record<string, string>>({})
  const [editing, setEditing] = useState(false)
  const [managing, setManaging] = useState(false)
  const [search, setSearch] = useState('')
  const [selectedDependencyId, setSelectedDependencyId] = useState('')

  const availableDependencies = useMemo(() => candidates.filter((candidate) => (
    candidate.id !== item.id
    && !details?.dependencyIds.includes(candidate.id)
    && candidate.name.toLocaleLowerCase().includes(search.toLocaleLowerCase())
  )), [candidates, details?.dependencyIds, item.id, search])

  useEffect(() => {
    if (!item.isBlocked || scope === todoScope.deleted) return

    let cancelled = false
    void onLoad(item.id, true).then(async (todo) => {
      if (!todo || cancelled) return
      const dependencies = await Promise.all(todo.dependencyIds.map(async (id) => ({
        id,
        todo: await onLoad(id, true),
      })))
      if (cancelled) return
      setDependencyNames(Object.fromEntries(dependencies
        .filter(({ todo }) => !todo || todo.status !== todoStatus.completed)
        .map(({ id, todo: dependency }) => [id, dependency?.name ?? 'Unavailable prerequisite'])))
    })

    return () => { cancelled = true }
  }, [item.id, item.isBlocked, onLoad, scope])

  async function openManager() {
    const loaded = await onLoad(item.id)
    if (loaded) {
      const dependencies = await Promise.all(loaded.dependencyIds.map(async (id) => ({
        id,
        todo: await onLoad(id, true),
      })))
      setDependencyNames(Object.fromEntries(dependencies.map(({ id, todo }) => [
        id,
        todo?.name ?? 'Unavailable prerequisite',
      ])))
      setDetails(loaded)
      setManaging(true)
    }
  }

  async function handleUpdate(draft: TodoDraft): Promise<boolean> {
    if (!details) return false
    const updated = await onUpdate(details, draft)
    if (!updated) return false
    setDetails(updated)
    setEditing(false)
    return true
  }

  async function handleStatus(status: TodoStatus) {
    if (!details) return
    const updated = await onStatus(details, status)
    if (updated) setDetails(updated)
  }

  async function handleAddDependency() {
    if (!details || !selectedDependencyId) return
    const updated = await onAddDependency(details, selectedDependencyId)
    if (updated) {
      const selected = candidates.find((candidate) => candidate.id === selectedDependencyId)
      setDependencyNames((current) => ({
        ...current,
        [selectedDependencyId]: selected?.name ?? 'Unavailable prerequisite',
      }))
      setDetails(updated)
      setSelectedDependencyId('')
      setSearch('')
    }
  }

  async function handleRemoveDependency(dependencyId: string) {
    if (!details) return
    const updated = await onRemoveDependency(details, dependencyId)
    if (updated) {
      setDependencyNames((current) => {
        const next = { ...current }
        delete next[dependencyId]
        return next
      })
      setDetails(updated)
    }
  }

  if (editing && details) {
    return (
      <article className="todo-card" data-testid={`todo-${item.id}`}>
        <TodoForm
          busy={busy}
          errors={errors}
          initial={{
            name: details.name,
            description: details.description ?? '',
            dueDate: details.dueDate,
            priority: details.priority,
          }}
          legend={`Edit ${details.name}`}
          submitLabel="Save changes"
          onCancel={() => setEditing(false)}
          onSubmit={handleUpdate}
        />
      </article>
    )
  }

  return (
    <article className="todo-card" data-testid={`todo-${item.id}`}>
      <div className="todo-card-heading">
        <div>
          <div className="badge-row">
            <span className={`priority priority-${item.priority}`}>
              {todoPriorityLabels[item.priority]}
            </span>
            <span className={`status status-${item.status}`}>
              {todoStatusLabels[item.status]}
            </span>
            {item.isRecurring && <span className="recurring">Repeats</span>}
            {item.isBlocked && <span className="blocked">Blocked</span>}
          </div>
          <h3>{item.name}</h3>
        </div>
        <span className="version">v{item.version}</span>
      </div>

      {item.descriptionPreview && <p>{item.descriptionPreview}</p>}
      {item.isBlocked && (
        <div className="blocked-note">
          <strong>{item.incompleteDependencyCount} incomplete prerequisite(s)</strong>
          {Object.values(dependencyNames).length > 0 && (
            <span>{Object.values(dependencyNames).join(', ')}</span>
          )}
        </div>
      )}
      <dl>
        <div><dt>Due</dt><dd>{item.dueDate}</dd></div>
        {scope === todoScope.deleted && (
          <>
            <div><dt>Deleted</dt><dd>{formatDateTime(item.deletedAt)}</dd></div>
            <div><dt>Purge</dt><dd>{formatDateTime(item.purgeAt)}</dd></div>
          </>
        )}
        <div><dt>ID</dt><dd className="todo-id">{item.id}</dd></div>
      </dl>

      <div className="card-actions">
        {scope === todoScope.deleted ? (
          <button
            className="button primary"
            disabled={busy}
            type="button"
            onClick={() => void onRestore(item)}
          >
            Restore
          </button>
        ) : (
          <>
            <button
              className="button secondary"
              disabled={busy}
              type="button"
              onClick={() => void openManager()}
            >
              {managing ? 'Refresh details' : 'Manage'}
            </button>
            <button
              className="button danger"
              disabled={busy}
              type="button"
              onClick={() => void onDelete(item)}
            >
              Delete
            </button>
          </>
        )}
      </div>

      {managing && details && (
        <section className="manage-panel" aria-label={`Manage ${item.name}`}>
          <label>
            Status
            <select
              aria-label={`Status for ${item.name}`}
              disabled={busy}
              value={details.status}
              onChange={(event) => void handleStatus(
                Number(event.target.value) as TodoStatus,
              )}
            >
              {Object.entries(todoStatusLabels).map(([value, label]) => (
                <option
                  key={value}
                  disabled={item.isBlocked && (
                    Number(value) === todoStatus.inProgress
                    || Number(value) === todoStatus.completed
                  )}
                  value={value}
                >
                  {label}
                </option>
              ))}
            </select>
          </label>

          {details.recurrence && (
            <p className="schedule-note">
              Occurrence {details.occurrenceNumber} · every {details.recurrence.interval}{' '}
              {['day', 'week', 'month'][details.recurrence.unit]}
              {details.recurrence.interval > 1 ? 's' : ''}
            </p>
          )}

          <div className="dependency-manager">
            <strong>Prerequisites</strong>
            {details.dependencyIds.length === 0 ? (
              <span className="muted">None selected</span>
            ) : (
              <ul>
                {details.dependencyIds.map((dependencyId) => (
                  <li key={dependencyId}>
                    <span>{dependencyNames[dependencyId] ?? dependencyId}</span>
                    <button
                      className="text-button"
                      disabled={busy}
                      type="button"
                      onClick={() => void handleRemoveDependency(dependencyId)}
                    >
                      Remove
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <label>
              Search loaded TODOs
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <div className="dependency-add-row">
              <select
                aria-label={`Dependency for ${item.name}`}
                value={selectedDependencyId}
                onChange={(event) => setSelectedDependencyId(event.target.value)}
              >
                <option value="">Choose a prerequisite</option>
                {availableDependencies.map((candidate) => (
                  <option key={candidate.id} value={candidate.id}>{candidate.name}</option>
                ))}
              </select>
              <button
                className="button secondary"
                disabled={busy || !selectedDependencyId}
                type="button"
                onClick={() => void handleAddDependency()}
              >
                Add
              </button>
            </div>
          </div>

          <div className="form-actions">
            <button
              className="button secondary"
              disabled={busy}
              type="button"
              onClick={() => setEditing(true)}
            >
              Edit details
            </button>
            <button
              className="button secondary"
              type="button"
              onClick={() => setManaging(false)}
            >
              Close
            </button>
          </div>
        </section>
      )}
    </article>
  )
}
