import { useEffect, useMemo, useState } from 'react'

import {
  todoPriority,
  todoPriorityLabels,
  todoScope,
  todoStatus,
  todoStatusLabels,
  type Todo,
  type TodoDraft,
  type TodoListItem,
  type TodoPriority,
  type TodoScope,
  type TodoStatus,
} from '../types/todo.ts'
import { useDebouncedValue } from '../hooks/useDebouncedValue.ts'
import styles from './TodoCard.module.scss'
import { TodoForm } from './TodoForm.tsx'
import { Badge, Button, type BadgeTone } from './common/index.ts'

type TodoCardProps = {
  busy: boolean
  /** Who created the TODO, when the Space's member list can name them. */
  creatorName?: string
  drifted?: boolean
  errors?: Record<string, string[]>
  item: TodoListItem
  /**
   * The viewer may look but not touch: no selection, no actions, no manage
   * panel. What the Space grants a Read member, and nothing the server would
   * not refuse anyway — this only keeps the refusals off the screen.
   */
  readOnly?: boolean
  scope: TodoScope
  selectable?: boolean
  selected?: boolean
  onToggleSelected?: (id: string) => void
  onAddDependency: (todo: Todo, dependencyId: string) => Promise<Todo | null>
  onDelete: (todo: TodoListItem) => Promise<boolean>
  onLoad: (id: string, quiet?: boolean) => Promise<Todo | null>
  /**
   * Reports whether this card has a Manage or Edit panel open. The page holds
   * off its background refreshes while any card does, because a refresh
   * replaces the list and would take an open panel — and whatever was typed
   * into it — down with it.
   */
  onPanelToggle?: (id: string, open: boolean) => void
  onRemoveDependency: (todo: Todo, dependencyId: string) => Promise<Todo | null>
  onRestore: (todo: TodoListItem) => Promise<boolean>
  onSearchCandidates: (search: string) => Promise<TodoListItem[]>
  onStatus: (todo: Todo, status: TodoStatus) => Promise<Todo | null>
  onUpdate: (todo: Todo, draft: TodoDraft) => Promise<Todo | null>
}

// Rising urgency, so a high priority reads the same as a blocking problem.
const priorityTones: Record<TodoPriority, BadgeTone> = {
  [todoPriority.low]: 'success',
  [todoPriority.medium]: 'warning',
  [todoPriority.high]: 'danger',
}

const statusTones: Record<TodoStatus, BadgeTone> = {
  [todoStatus.open]: 'neutral',
  [todoStatus.inProgress]: 'info',
  [todoStatus.completed]: 'success',
  [todoStatus.archived]: 'pending',
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
  creatorName,
  drifted = false,
  errors,
  item,
  readOnly = false,
  scope,
  selectable = false,
  selected = false,
  onAddDependency,
  onDelete,
  onLoad,
  onPanelToggle,
  onRemoveDependency,
  onRestore,
  onSearchCandidates,
  onStatus,
  onToggleSelected,
  onUpdate,
}: TodoCardProps) {
  const [details, setDetails] = useState<Todo | null>(null)
  const [dependencyNames, setDependencyNames] = useState<Record<string, string>>({})
  const [editing, setEditing] = useState(false)
  const [managing, setManaging] = useState(false)
  const [search, setSearch] = useState('')
  const [candidates, setCandidates] = useState<TodoListItem[]>([])
  const [searching, setSearching] = useState(false)
  const [selectedDependencyId, setSelectedDependencyId] = useState('')
  const debouncedSearch = useDebouncedValue(search)

  // The text predicate now runs on the server. What stays here is what the
  // server cannot know: this card's own identity and the prerequisites it
  // already has.
  const availableDependencies = useMemo(() => candidates.filter((candidate) => (
    candidate.id !== item.id
    && !details?.dependencyIds.includes(candidate.id)
  )), [candidates, details?.dependencyIds, item.id])

  // An archived TODO is frozen in the domain: editing it, changing its
  // dependencies, or completing it are rejected. Only the transitions that
  // unarchive it, and deletion, remain.
  const frozen = item.status === todoStatus.archived

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

  // Only while the panel is open: a page of cards would otherwise each hold a
  // candidate list nobody is looking at.
  useEffect(() => {
    if (!managing || frozen) return

    let cancelled = false
    setSearching(true)

    // The previous options stay on screen while this runs. Clearing them would
    // make the list flicker empty on every pause in typing, so the group is
    // marked busy instead and the stale options remain selectable.
    void onSearchCandidates(debouncedSearch).then((found) => {
      if (!cancelled) setCandidates(found)
    }).catch(() => {
      if (!cancelled) setCandidates([])
    }).finally(() => {
      if (!cancelled) setSearching(false)
    })

    return () => { cancelled = true }
  }, [debouncedSearch, frozen, managing, onSearchCandidates])

  // A selection made before the list narrowed can stop being offered. Left set,
  // Add would send an identifier the picker no longer shows.
  useEffect(() => {
    if (!selectedDependencyId) return
    if (availableDependencies.some((candidate) => candidate.id === selectedDependencyId)) {
      return
    }

    setSelectedDependencyId('')
  }, [availableDependencies, selectedDependencyId])

  // Reported on every change and withdrawn on unmount, so the page's count of
  // open panels cannot outlive the card that opened one.
  useEffect(() => {
    onPanelToggle?.(item.id, managing || editing)
    return () => onPanelToggle?.(item.id, false)
  }, [editing, item.id, managing, onPanelToggle])

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
      const selected = candidates.find(
        (candidate) => candidate.id === selectedDependencyId,
      )
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

  // A form left open across a downgrade to Read would still offer Save; the
  // server would refuse it, but the card should not offer what it cannot do.
  if (editing && details && !readOnly) {
    return (
      <article
      className={drifted ? `${styles.todoCard} ${styles.drifted}` : styles.todoCard}
      data-testid={`todo-${item.id}`}
    >
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
    <article
      className={drifted ? `${styles.todoCard} ${styles.drifted}` : styles.todoCard}
      data-testid={`todo-${item.id}`}
    >
      <div className={styles.todoCardHeading}>
        <div>
          <div className={styles.badgeRow}>
            {selectable && !readOnly && (
              <label className={styles.selectBox}>
                <input
                  checked={selected}
                  disabled={busy}
                  type="checkbox"
                  onChange={() => onToggleSelected?.(item.id)}
                />
                <span className={styles.visuallyHidden}>Select {item.name}</span>
              </label>
            )}
            <Badge tone={priorityTones[item.priority]}>
              {todoPriorityLabels[item.priority]}
            </Badge>
            <Badge tone={statusTones[item.status]}>
              {todoStatusLabels[item.status]}
            </Badge>
            {item.isRecurring && <Badge tone="accent">Repeats</Badge>}
            {item.isBlocked && <Badge tone="danger">Blocked</Badge>}
          </div>
          <h3>{item.name}</h3>
        </div>
        <Badge tone="version">v{item.version}</Badge>
      </div>

      {item.descriptionPreview && <p>{item.descriptionPreview}</p>}
      {item.isBlocked && (
        <div className={styles.blockedNote}>
          <strong>{item.incompleteDependencyCount} incomplete prerequisite(s)</strong>
          {Object.values(dependencyNames).length > 0 && (
            <span>{Object.values(dependencyNames).join(', ')}</span>
          )}
        </div>
      )}
      <dl>
        <div><dt>Due</dt><dd>{item.dueDate}</dd></div>
        {/*
          Absent rather than blank when the creator cannot be named: they may
          have left the Space, or the member list may not have loaded yet, and
          neither is worth a row that says nothing.
        */}
        {creatorName && (
          <div><dt>By</dt><dd data-testid="created-by">{creatorName}</dd></div>
        )}
        {scope === todoScope.deleted && (
          <>
            <div><dt>Deleted</dt><dd>{formatDateTime(item.deletedAt)}</dd></div>
            <div><dt>Purge</dt><dd>{formatDateTime(item.purgeAt)}</dd></div>
          </>
        )}
        <div>
          <dt>ID</dt>
          {/*
            Not `todo-…`: that prefix identifies a card, and the browser suite
            counts cards with [data-testid^="todo-"].
          */}
          <dd className={styles.todoId} data-testid="record-id">{item.id}</dd>
        </div>
      </dl>

      {!readOnly && (
        <div className={styles.cardActions}>
          {scope === todoScope.deleted ? (
            <Button
              variant="primary"
              disabled={busy}
              onClick={() => void onRestore(item)}
            >
              Restore
            </Button>
          ) : (
            <>
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => void openManager()}
              >
                {managing ? 'Refresh details' : 'Manage'}
              </Button>
              <Button
                variant="danger"
                disabled={busy}
                onClick={() => void onDelete(item)}
              >
                Delete
              </Button>
            </>
          )}
        </div>
      )}

      {managing && details && !readOnly && (
        <section className={styles.managePanel} aria-label={`Manage ${item.name}`}>
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
              {Object.entries(todoStatusLabels)
                .filter(([value]) => !frozen || Number(value) !== todoStatus.completed)
                .map(([value, label]) => (
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
            <p className={styles.scheduleNote}>
              Occurrence {details.occurrenceNumber} · every {details.recurrence.interval}{' '}
              {['day', 'week', 'month'][details.recurrence.unit]}
              {details.recurrence.interval > 1 ? 's' : ''}
            </p>
          )}

          {frozen && (
            <p className={styles.scheduleNote}>
              Archived TODOs are frozen. Unarchive to edit details or change
              prerequisites.
            </p>
          )}

          {!frozen && (
          <div className={styles.dependencyManager}>
            <strong>Prerequisites</strong>
            {details.dependencyIds.length === 0 ? (
              <span className={styles.muted}>None selected</span>
            ) : (
              <ul>
                {details.dependencyIds.map((dependencyId) => (
                  <li key={dependencyId}>
                    <span>{dependencyNames[dependencyId] ?? dependencyId}</span>
                    <Button
                      variant="text"
                      disabled={busy}
                      onClick={() => void handleRemoveDependency(dependencyId)}
                    >
                      Remove
                    </Button>
                  </li>
                ))}
              </ul>
            )}
            <label>
              Search TODOs
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
              />
            </label>
            <div aria-busy={searching} className={styles.dependencyAddRow}>
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
              <Button
                variant="secondary"
                disabled={busy || !selectedDependencyId}
                onClick={() => void handleAddDependency()}
              >
                Add
              </Button>
              {searching && <span className={styles.muted}>Searching…</span>}
            </div>
          </div>
          )}

          <div className={styles.cardActions}>
            {!frozen && (
              <Button
                variant="secondary"
                disabled={busy}
                onClick={() => setEditing(true)}
              >
                Edit details
              </Button>
            )}
            <Button variant="secondary" onClick={() => setManaging(false)}>
              Close
            </Button>
          </div>
        </section>
      )}
    </article>
  )
}
