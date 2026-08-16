import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'

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
} from '../api/todos.ts'
import { useAuth } from '../auth/AuthContext.ts'
import { AssistantPanel } from '../components/AssistantPanel.tsx'
import { BulkDeleteDialog } from '../components/BulkDeleteDialog.tsx'
import { BulkToolbar } from '../components/BulkToolbar.tsx'
import { CreateTodoForm } from '../components/CreateTodoForm.tsx'
import { TodoCard } from '../components/TodoCard.tsx'
import { UserMenu } from '../components/UserMenu.tsx'
import { Button, EmptyState, Toast, ToastRegion } from '../components/common/index.ts'
import { useBulkActions, type BulkOperation } from '../hooks/useBulkActions.ts'
import { useDebouncedValue } from '../hooks/useDebouncedValue.ts'
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
  type TodoVersionReference,
} from '../types/todo.ts'
import styles from './TodosPage.module.scss'
import { mergeTodoPage } from './todoPageMerge.ts'

/**
 * `dismissed` hides the toast without discarding the error. The same problem
 * also supplies the per-field messages under the create form and the affected
 * card, so clearing the state outright would take the validation the user is
 * still working through with it.
 */
type UiError = {
  affectedTodoId?: string
  dismissed?: boolean
  kind: ApiErrorKind
  problem: ProblemDetails
}

const initialFilters: Omit<TodoListOptions, 'scope' | 'cursor'> = {
  status: null,
  priority: null,
  dueFrom: '',
  dueTo: '',
  dependencyStatus: null,
  searchText: '',
  sortField: todoSortField.dueDate,
  sortDirection: sortDirection.ascending,
  limit: 12,
}

/** How many prerequisites the dependency picker asks the server for at a time. */
const dependencySearchLimit = 50

/** How long a success toast stays on screen before it clears itself. */
const noticeDuration = 5000

const tabs: { label: string; scope: TodoScope }[] = [
  { label: 'Active', scope: todoScope.active },
  { label: 'Archived', scope: todoScope.archived },
  { label: 'Trash', scope: todoScope.deleted },
]

export function TodosPage() {
  const [scope, setScope] = useState<TodoScope>(todoScope.active)
  const [filters, setFilters] = useState(initialFilters)
  const [items, setItems] = useState<TodoListItem[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<UiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [pendingDelete, setPendingDelete] = useState<TodoVersionReference[] | null>(null)

  // The box's own state. Only its debounced value reaches `filters`, so a
  // keystroke never invalidates a cursor the Load more button is still holding.
  const [searchInput, setSearchInput] = useState('')
  const debouncedSearch = useDebouncedValue(searchInput)
  const navigate = useNavigate()
  const { endSession } = useAuth()

  const reloadList = useCallback(() => {
    setRefreshKey((current) => current + 1)
  }, [])

  const bulk = useBulkActions({ items, onRefresh: reloadList })

  const captureError = useCallback((caught: unknown, affectedTodoId?: string) => {
    const apiError = caught instanceof ApiError
      ? caught
      : new ApiError(0, {
        title: 'Unable to reach the API.',
        detail: 'Check that the API and MongoDB are running.',
      })

    // An expired or missing session is not a page-level error: the only useful
    // response is to start the login flow again. The client's own session state
    // has to be dropped first, or the login route sees a still-authenticated
    // user and sends it straight back to a page every request now rejects.
    if (apiError.kind === 'unauthorized') {
      endSession()
      void navigate('/login', { replace: true })
      return
    }

    setError({
      affectedTodoId,
      kind: apiError.kind,
      problem: apiError.problem,
    })
  }, [endSession, navigate])

  const loadTodo = useCallback(async (id: string, quiet = false): Promise<Todo | null> => {
    try {
      return await getTodo(id)
    } catch (caught) {
      if (!quiet) captureError(caught, id)
      return null
    }
  }, [captureError])

  // Bails out when nothing changed rather than spreading unconditionally. A new
  // object here would be a new value for the load effect below, so mount and
  // the moment Clear's reset drains through the debounce would each fire a
  // second identical request racing the first.
  useEffect(() => {
    setFilters((current) => (
      current.searchText === debouncedSearch
        ? current
        : { ...current, searchText: debouncedSearch }
    ))
  }, [debouncedSearch])

  // Only a success notice expires. An error carries a trace ID and sometimes the
  // reload action, so it waits for the user rather than a timer.
  //
  // Every path that sets a notice clears it before its await, so the value
  // always passes through null and the timer re-arms even when the same message
  // is reported twice in a row.
  useEffect(() => {
    const timer = notice === null
      ? null
      : setTimeout(() => setNotice(null), noticeDuration)

    return () => {
      if (timer !== null) clearTimeout(timer)
    }
  }, [notice])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)

    // Dropped before the request rather than after it. The cursor was minted
    // under the previous filters, so leaving it in place would let Load more
    // send it alongside the new ones and be refused.
    setNextCursor(null)

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

  /**
   * Searches active TODOs for the dependency picker. The server does the
   * matching, so a prerequisite outside the first page of the list is
   * reachable — which the picker's old client-side filter over a fixed
   * hundred candidates was not.
   */
  const searchCandidates = useCallback(async (search: string) => {
    const page = await listTodos({
      ...initialFilters,
      scope: todoScope.active,
      sortField: todoSortField.name,
      limit: dependencySearchLimit,
      searchText: search,
    })

    return page.items
  }, [])

  // Returning to the tab is when another tab's edits are most likely to have
  // landed. Refreshing then keeps versions fresh, but only while nothing is
  // selected: a selection resolves its versions from what is on screen, so
  // changing the list underneath one would send versions the user never saw.
  useEffect(() => {
    function revalidate() {
      if (document.visibilityState !== 'visible') return
      if (bulk.selectedCount > 0 || bulk.bulkBusy || busyId !== null) return
      reloadList()
    }

    window.addEventListener('focus', revalidate)
    document.addEventListener('visibilitychange', revalidate)
    return () => {
      window.removeEventListener('focus', revalidate)
      document.removeEventListener('visibilitychange', revalidate)
    }
  }, [busyId, bulk.bulkBusy, bulk.selectedCount, reloadList])

  function selectScope(nextScope: TodoScope) {
    setScope(nextScope)
    setItems([])
    setNextCursor(null)
    setNotice(null)
    setError(null)
    bulk.clearSelection()
  }

  async function loadMore() {
    if (!nextCursor) return
    setLoadingMore(true)
    setError(null)
    try {
      const page = await listTodos({ ...filters, scope, cursor: nextCursor })
      setItems((current) => mergeTodoPage(current, page.items))
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

  async function runBulk(operation: BulkOperation, selection?: TodoVersionReference[]) {
    setError(null)
    setNotice(null)

    // The summary is counted against what the batch actually sent rather than
    // what was selected, because a silent retry resends re-read versions.
    const outcome = await bulk.run(operation, selection)
    if (outcome.kind === 'done') {
      const summary = bulk.finish(outcome.result, outcome.sent)
      const parts = [`${summary.changed} updated`]
      if (summary.unchanged > 0) parts.push(`${summary.unchanged} already up to date`)
      if (summary.occurrences > 0) {
        parts.push(`${summary.occurrences} recurring occurrence(s) created`)
      }
      setNotice(`${parts.join(', ')}.`)
      return
    }

    // A repair is not an error banner: the selection survives so the user can
    // review the highlighted cards and retry, which the toolbar still offers.
    if (outcome.kind === 'failed' && outcome.error !== null) {
      captureError(outcome.error)
    }
  }

  function requestBulkDelete() {
    setError(null)
    setNotice(null)
    setPendingDelete(bulk.buildSelection())
  }

  // The dialog stays open until the request settles, so it can disable its own
  // actions and report progress rather than vanishing while the batch runs.
  async function confirmBulkDelete(selection: TodoVersionReference[]) {
    await runBulk({ kind: 'delete' }, selection)
    setPendingDelete(null)
  }

  const errorTitle = error?.kind === 'concurrency'
    ? 'This TODO was changed by another user.'
    : error?.problem.title ?? 'Something went wrong.'

  return (
    <main className={styles.appShell}>
      <header className={styles.pageHeader}>
        <div>
          <p className={styles.eyebrow}>Sleeky To-Do</p>
          <h1>Keep today clear.</h1>
          <p>Plan dependencies, repeat the work that matters, and recover safely.</p>
        </div>
        <div className={styles.sessionNote}>
          <strong>Persisted workspace</strong>
          <span>Cursor pages stay current with MongoDB and optimistic versions.</span>
          <UserMenu />
        </div>
      </header>

      {/*
        Stays in flow rather than joining the toasts. It is not an event that
        has passed: it captions the ringed cards below and survives until the
        selection is cleared, so the user can read it against them.
      */}
      {bulk.repair && (
        <section className={styles.repairBanner} role="alert">
          <div>
            <strong>The selection is out of date.</strong>
            <p>{bulk.repair.message}</p>
          </div>
        </section>
      )}

      {scope === todoScope.active && (
        <section className={styles.createPanel}>
          <CreateTodoForm
            busy={busyId === 'create'}
            errors={!error?.affectedTodoId ? error?.problem.errors : undefined}
            onSubmit={handleCreate}
          />
        </section>
      )}

      {/*
        The assistant refreshes the list through the same callback the bulk
        toolbar uses, because its writes are the same writes.
      */}
      <AssistantPanel onTodosChanged={reloadList} />

      <div className={styles.scopeTabs} aria-label="TODO scopes" role="tablist">
        {tabs.map((tab) => (
          <button
            aria-selected={scope === tab.scope}
            className={styles.scopeTab}
            key={tab.scope}
            role="tab"
            type="button"
            onClick={() => selectScope(tab.scope)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <section className={styles.filterPanel} aria-label="TODO filters">
        <label className={styles.searchField}>
          Search
          <input
            aria-label="Search filter"
            type="search"
            placeholder="Words from the name or description"
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
          />
        </label>
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
            <option value={todoStatus.open}>Open</option>
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
        <Button
          variant="secondary"
          className={styles.clearFilters}
          onClick={() => {
            // The box holds its own state, so resetting `filters` alone would
            // leave the typed text on screen and re-apply it on the next
            // keystroke while the list showed everything.
            setSearchInput('')
            setFilters(initialFilters)
          }}
        >
          Clear filters
        </Button>
      </section>

      <section
        className={styles.todoSection}
        aria-label={tabs.find((tab) => tab.scope === scope)?.label}
      >
        <div className={styles.sectionHeading}>
          <h2>{tabs.find((tab) => tab.scope === scope)?.label}</h2>
          <span>{items.length}</span>
        </div>
        <BulkToolbar
          busy={bulk.bulkBusy || busyId !== null}
          loadedCount={items.length}
          overLimit={bulk.overLimit}
          scope={scope}
          selectedCount={bulk.selectedCount}
          selectedStatuses={bulk.selectedStatuses}
          onDelete={requestBulkDelete}
          onRestore={() => void runBulk({ kind: 'restore' })}
          onSelectLoaded={bulk.selectLoaded}
          onStatus={(status) => void runBulk({ kind: 'status', status })}
        />

        {loading ? (
          <EmptyState>Loading TODOs…</EmptyState>
        ) : items.length === 0 ? (
          <EmptyState>
            {filters.searchText
              ? 'No TODOs match your search.'
              : 'No TODOs match this view.'}
          </EmptyState>
        ) : (
          <div className={styles.todoGrid}>
            {items.map((item) => (
              <TodoCard
                key={`${item.id}:${item.version}`}
                busy={busyId === item.id || bulk.bulkBusy}
                drifted={bulk.repair?.driftedIds.includes(item.id) ?? false}
                errors={error?.affectedTodoId === item.id ? error.problem.errors : undefined}
                item={item}
                scope={scope}
                selectable
                selected={bulk.selectedIds.has(item.id)}
                onAddDependency={handleAddDependency}
                onDelete={handleDelete}
                onLoad={loadTodo}
                onRemoveDependency={handleRemoveDependency}
                onRestore={handleRestore}
                onSearchCandidates={searchCandidates}
                onStatus={handleStatus}
                onToggleSelected={bulk.toggle}
                onUpdate={handleUpdate}
              />
            ))}
          </div>
        )}

        {/*
          Hidden while a fresh first page is in flight. The cursor on screen
          belongs to the page being replaced, so offering it during that window
          is offering a continuation of a list that no longer exists.
        */}
        {nextCursor && !loading && (
          <div className={styles.loadMoreRow}>
            <Button
              variant="primary"
              disabled={loadingMore}
              onClick={() => void loadMore()}
            >
              {loadingMore ? 'Loading…' : 'Load more'}
            </Button>
          </div>
        )}
      </section>

      {pendingDelete && (
        <BulkDeleteDialog
          busy={bulk.bulkBusy}
          selection={pendingDelete}
          onCancel={() => setPendingDelete(null)}
          onConfirm={(selection) => void confirmBulkDelete(selection)}
        />
      )}

      {/*
        The error is rendered last so it sits against the bottom edge the region
        is anchored to. Notices come and go above it, which leaves the one toast
        the user may still be reading exactly where they found it.
      */}
      <ToastRegion>
        {notice && (
          <Toast title={notice} tone="notice" onDismiss={() => setNotice(null)} />
        )}
        {error && !error.dismissed && (
          <Toast
            detail={error.problem.detail}
            meta={error.problem.traceId ? `Trace: ${error.problem.traceId}` : undefined}
            title={errorTitle}
            tone="error"
            onDismiss={() => setError((current) => (
              current === null ? null : { ...current, dismissed: true }
            ))}
          >
            {/* Not gated on affectedTodoId: a bulk conflict is captured without
                one, and reloading is the recovery for both. */}
            {error.kind === 'concurrency' && (
              <Button variant="secondary" onClick={reloadLatestVersion}>
                Reload latest version
              </Button>
            )}
          </Toast>
        )}
      </ToastRegion>
    </main>
  )
}
