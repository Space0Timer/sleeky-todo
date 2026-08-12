import { useCallback, useEffect, useState } from 'react'

import './App.css'
import {
  ApiError,
  addTodoDependency,
  changeTodoStatus,
  createTodo,
  deleteTodo,
  getTodo,
  listTodos,
  removeTodoDependency,
  restoreTodo,
  updateTodo,
} from './api/todos.ts'
import { CreateTodoForm } from './components/CreateTodoForm.tsx'
import { TodoCard } from './components/TodoCard.tsx'
import {
  dependencyStatus,
  sortDirection,
  todoPriority,
  todoScope,
  todoSortField,
  todoStatus,
  type ApiErrorKind,
  type CreateTodoDraft,
  type ProblemDetails,
  type Todo,
  type TodoDraft,
  type TodoListItem,
  type TodoListOptions,
  type TodoScope,
  type TodoStatus,
} from './types/todo.ts'

type UiError = {
  affectedTodoId?: string
  kind: ApiErrorKind
  problem: ProblemDetails
}

const initialFilters: Omit<TodoListOptions, 'scope' | 'cursor'> = {
  status: null,
  priority: null,
  dueFrom: '',
  dueTo: '',
  dependencyStatus: null,
  sortField: todoSortField.dueDate,
  sortDirection: sortDirection.ascending,
  limit: 12,
}

const tabs: { label: string; scope: TodoScope }[] = [
  { label: 'Active', scope: todoScope.active },
  { label: 'Archived', scope: todoScope.archived },
  { label: 'Trash', scope: todoScope.deleted },
]

function App() {
  const [scope, setScope] = useState<TodoScope>(todoScope.active)
  const [filters, setFilters] = useState(initialFilters)
  const [items, setItems] = useState<TodoListItem[]>([])
  const [dependencyCandidates, setDependencyCandidates] = useState<TodoListItem[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<UiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const captureError = useCallback((caught: unknown, affectedTodoId?: string) => {
    const apiError = caught instanceof ApiError
      ? caught
      : new ApiError(0, {
        title: 'Unable to reach the API.',
        detail: 'Check that the API and MongoDB are running.',
      })
    setError({
      affectedTodoId,
      kind: apiError.kind,
      problem: apiError.problem,
    })
  }, [])

  const loadTodo = useCallback(async (id: string, quiet = false): Promise<Todo | null> => {
    try {
      return await getTodo(id)
    } catch (caught) {
      if (!quiet) captureError(caught, id)
      return null
    }
  }, [captureError])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)

    void listTodos({ ...filters, scope }).then((page) => {
      if (cancelled) return
      setItems(page.items)
      setNextCursor(page.nextCursor)
    }).catch((caught: unknown) => {
      if (!cancelled) captureError(caught)
    }).finally(() => {
      if (!cancelled) setLoading(false)
    })

    return () => { cancelled = true }
  }, [captureError, filters, refreshKey, scope])

  useEffect(() => {
    let cancelled = false
    void listTodos({
      ...initialFilters,
      scope: todoScope.active,
      sortField: todoSortField.name,
      limit: 100,
    }).then((page) => {
      if (!cancelled) setDependencyCandidates(page.items)
    }).catch(() => {
      if (!cancelled) setDependencyCandidates([])
    })
    return () => { cancelled = true }
  }, [refreshKey])

  function reloadList() {
    setRefreshKey((current) => current + 1)
  }

  function selectScope(nextScope: TodoScope) {
    setScope(nextScope)
    setItems([])
    setNextCursor(null)
    setNotice(null)
    setError(null)
  }

  async function loadMore() {
    if (!nextCursor) return
    setLoadingMore(true)
    setError(null)
    try {
      const page = await listTodos({ ...filters, scope, cursor: nextCursor })
      setItems((current) => [...current, ...page.items])
      setNextCursor(page.nextCursor)
    } catch (caught) {
      captureError(caught)
    } finally {
      setLoadingMore(false)
    }
  }

  async function handleCreate(draft: CreateTodoDraft): Promise<boolean> {
    setBusyId('create')
    setError(null)
    setNotice(null)
    try {
      await createTodo(draft)
      setScope(todoScope.active)
      setNotice('TODO created.')
      reloadList()
      return true
    } catch (caught) {
      captureError(caught)
      return false
    } finally {
      setBusyId(null)
    }
  }

  async function mutateTodo(
    todo: Todo,
    operation: () => Promise<Todo>,
  ): Promise<Todo | null> {
    setBusyId(todo.id)
    setError(null)
    setNotice(null)
    try {
      const updated = await operation()
      if (updated.nextOccurrenceId) {
        setNotice(`Completed. Next occurrence: ${updated.nextOccurrenceId}`)
      }
      reloadList()
      return updated
    } catch (caught) {
      captureError(caught, todo.id)
      return null
    } finally {
      setBusyId(null)
    }
  }

  function handleUpdate(todo: Todo, draft: TodoDraft) {
    return mutateTodo(todo, () => updateTodo(todo, draft))
  }

  function handleStatus(todo: Todo, status: TodoStatus) {
    return mutateTodo(todo, () => changeTodoStatus(todo, status))
  }

  function handleAddDependency(todo: Todo, dependencyId: string) {
    return mutateTodo(todo, () => addTodoDependency(todo, dependencyId))
  }

  function handleRemoveDependency(todo: Todo, dependencyId: string) {
    return mutateTodo(todo, () => removeTodoDependency(todo, dependencyId))
  }

  async function handleDelete(todo: TodoListItem): Promise<boolean> {
    setBusyId(todo.id)
    setError(null)
    setNotice(null)
    try {
      await deleteTodo(todo)
      setNotice('TODO moved to Trash.')
      reloadList()
      return true
    } catch (caught) {
      captureError(caught, todo.id)
      return false
    } finally {
      setBusyId(null)
    }
  }

  async function handleRestore(todo: TodoListItem): Promise<boolean> {
    setBusyId(todo.id)
    setError(null)
    setNotice(null)
    try {
      await restoreTodo(todo)
      setNotice('TODO restored to Active.')
      reloadList()
      return true
    } catch (caught) {
      captureError(caught, todo.id)
      return false
    } finally {
      setBusyId(null)
    }
  }

  function reloadLatestVersion() {
    setError(null)
    reloadList()
  }

  const errorTitle = error?.kind === 'concurrency'
    ? 'This TODO was changed by another user.'
    : error?.problem.title ?? 'Something went wrong.'

  return (
    <main className="app-shell">
      <header className="page-header">
        <div>
          <p className="eyebrow">Sleeky To-Do</p>
          <h1>Keep today clear.</h1>
          <p>Plan dependencies, repeat the work that matters, and recover safely.</p>
        </div>
        <div className="session-note">
          <strong>Persisted workspace</strong>
          <span>Cursor pages stay current with MongoDB and optimistic versions.</span>
        </div>
      </header>

      {error && (
        <section className={`error-banner error-${error.kind}`} role="alert">
          <div>
            <strong>{errorTitle}</strong>
            <p>{error.problem.detail}</p>
            {error.problem.traceId && <small>Trace: {error.problem.traceId}</small>}
          </div>
          {error.kind === 'concurrency' && error.affectedTodoId && (
            <button
              className="button secondary"
              type="button"
              onClick={reloadLatestVersion}
            >
              Reload latest version
            </button>
          )}
        </section>
      )}

      {notice && <section className="notice-banner" role="status">{notice}</section>}

      {scope === todoScope.active && (
        <section className="create-panel">
          <CreateTodoForm
            busy={busyId === 'create'}
            errors={!error?.affectedTodoId ? error?.problem.errors : undefined}
            onSubmit={handleCreate}
          />
        </section>
      )}

      <nav className="scope-tabs" aria-label="TODO scopes" role="tablist">
        {tabs.map((tab) => (
          <button
            aria-selected={scope === tab.scope}
            className="scope-tab"
            key={tab.scope}
            role="tab"
            type="button"
            onClick={() => selectScope(tab.scope)}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      <section className="filter-panel" aria-label="TODO filters">
        <label>
          Status
          <select
            aria-label="Status filter"
            value={filters.status ?? ''}
            onChange={(event) => setFilters({
              ...filters,
              status: event.target.value === ''
                ? null
                : Number(event.target.value) as TodoStatus,
            })}
          >
            <option value="">All statuses</option>
            <option value={todoStatus.notStarted}>Not started</option>
            <option value={todoStatus.inProgress}>In progress</option>
            <option value={todoStatus.completed}>Completed</option>
            <option value={todoStatus.archived}>Archived</option>
          </select>
        </label>
        <label>
          Priority
          <select
            aria-label="Priority filter"
            value={filters.priority ?? ''}
            onChange={(event) => setFilters({
              ...filters,
              priority: event.target.value === ''
                ? null
                : Number(event.target.value) as TodoListOptions['priority'],
            })}
          >
            <option value="">All priorities</option>
            <option value={todoPriority.low}>Low</option>
            <option value={todoPriority.medium}>Medium</option>
            <option value={todoPriority.high}>High</option>
          </select>
        </label>
        <label>
          Due from
          <input
            aria-label="Due from filter"
            type="date"
            value={filters.dueFrom}
            onChange={(event) => setFilters({ ...filters, dueFrom: event.target.value })}
          />
        </label>
        <label>
          Due to
          <input
            aria-label="Due to filter"
            type="date"
            value={filters.dueTo}
            onChange={(event) => setFilters({ ...filters, dueTo: event.target.value })}
          />
        </label>
        <label>
          Dependencies
          <select
            aria-label="Dependency filter"
            value={filters.dependencyStatus ?? ''}
            onChange={(event) => setFilters({
              ...filters,
              dependencyStatus: event.target.value === ''
                ? null
                : Number(event.target.value) as TodoListOptions['dependencyStatus'],
            })}
          >
            <option value="">Blocked and unblocked</option>
            <option value={dependencyStatus.blocked}>Blocked</option>
            <option value={dependencyStatus.unblocked}>Unblocked</option>
          </select>
        </label>
        <label>
          Sort by
          <select
            aria-label="Sort field"
            value={filters.sortField}
            onChange={(event) => setFilters({
              ...filters,
              sortField: Number(event.target.value) as TodoListOptions['sortField'],
            })}
          >
            <option value={todoSortField.dueDate}>Due date</option>
            <option value={todoSortField.priority}>Priority</option>
            <option value={todoSortField.status}>Status</option>
            <option value={todoSortField.name}>Name</option>
          </select>
        </label>
        <label>
          Direction
          <select
            aria-label="Sort direction"
            value={filters.sortDirection}
            onChange={(event) => setFilters({
              ...filters,
              sortDirection: Number(event.target.value) as TodoListOptions['sortDirection'],
            })}
          >
            <option value={sortDirection.ascending}>Ascending</option>
            <option value={sortDirection.descending}>Descending</option>
          </select>
        </label>
        <button
          className="button secondary clear-filters"
          type="button"
          onClick={() => setFilters(initialFilters)}
        >
          Clear filters
        </button>
      </section>

      <section
        className="todo-section"
        aria-label={tabs.find((tab) => tab.scope === scope)?.label}
      >
        <div className="section-heading">
          <h2>{tabs.find((tab) => tab.scope === scope)?.label}</h2>
          <span>{items.length}</span>
        </div>
        {loading ? (
          <p className="empty-state">Loading TODOs…</p>
        ) : items.length === 0 ? (
          <p className="empty-state">No TODOs match this view.</p>
        ) : (
          <div className="todo-grid">
            {items.map((item) => (
              <TodoCard
                key={`${item.id}:${item.version}`}
                busy={busyId === item.id}
                candidates={dependencyCandidates}
                errors={error?.affectedTodoId === item.id ? error.problem.errors : undefined}
                item={item}
                scope={scope}
                onAddDependency={handleAddDependency}
                onDelete={handleDelete}
                onLoad={loadTodo}
                onRemoveDependency={handleRemoveDependency}
                onRestore={handleRestore}
                onStatus={handleStatus}
                onUpdate={handleUpdate}
              />
            ))}
          </div>
        )}

        {nextCursor && (
          <div className="load-more-row">
            <button
              className="button primary"
              disabled={loadingMore}
              type="button"
              onClick={() => void loadMore()}
            >
              {loadingMore ? 'Loading…' : 'Load more'}
            </button>
          </div>
        )}
      </section>
    </main>
  )
}

export default App
