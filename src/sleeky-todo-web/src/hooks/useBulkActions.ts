import { useCallback, useMemo, useState } from 'react'

import {
  ApiError,
  bulkChangeTodoStatus,
  bulkDeleteTodos,
  bulkRestoreTodos,
  lookupTodoSelection,
} from '../api/todos.ts'
import {
  maximumBulkSelection,
  type BulkTodoResult,
  type Todo,
  type TodoListItem,
  type TodoStatus,
  type TodoVersionReference,
} from '../types/todo.ts'

export type BulkOperation =
  | { kind: 'status'; status: TodoStatus }
  | { kind: 'delete' }
  | { kind: 'restore' }

export type BulkRepair = {
  driftedIds: string[]
  message: string
  vanishedCount: number
}

type BulkOutcome =
  | { kind: 'done'; result: BulkTodoResult }
  | { kind: 'repair'; repair: BulkRepair }
  | { kind: 'failed'; error: unknown }

type UseBulkActionsOptions = {
  items: TodoListItem[]
  onRefresh: () => void
}

/**
 * A batch is all-or-nothing and version-checked, so a selection holds
 * identifiers only and resolves versions from list state at the moment an
 * action runs. That keeps "the version sent" equal to "the version the user
 * last saw", which the conflict paths below rely on.
 */
export function useBulkActions({ items, onRefresh }: UseBulkActionsOptions) {
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [bulkBusy, setBulkBusy] = useState(false)
  const [repair, setRepair] = useState<BulkRepair | null>(null)

  const selectedCount = selectedIds.size
  const overLimit = selectedCount > maximumBulkSelection

  const selectedStatuses = useMemo(() => {
    const statuses = new Set<TodoStatus>()
    for (const item of items) {
      if (selectedIds.has(item.id)) statuses.add(item.status)
    }
    return statuses
  }, [items, selectedIds])

  const clearSelection = useCallback(() => {
    setSelectedIds(new Set())
    setRepair(null)
  }, [])

  const toggle = useCallback((id: string) => {
    setSelectedIds((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }, [])

  const selectLoaded = useCallback((select: boolean) => {
    setSelectedIds(select ? new Set(items.map((item) => item.id)) : new Set())
  }, [items])

  /** Versions come from what is on screen, never from a snapshot taken earlier. */
  const buildSelection = useCallback((): TodoVersionReference[] => items
    .filter((item) => selectedIds.has(item.id))
    .map((item) => ({ id: item.id, version: item.version })), [items, selectedIds])

  const send = useCallback((
    operation: BulkOperation,
    selection: TodoVersionReference[],
  ): Promise<BulkTodoResult> => {
    if (operation.kind === 'delete') return bulkDeleteTodos(selection)
    if (operation.kind === 'restore') return bulkRestoreTodos(selection)
    return bulkChangeTodoStatus(operation.status, selection)
  }, [])

  /**
   * Reads the selection's current state without touching the list, so the
   * user's view stays exactly as they left it while a conflict is diagnosed.
   */
  const hydrate = useCallback(async (ids: string[]): Promise<Todo[] | null> => {
    try {
      const selection = await lookupTodoSelection(ids)
      return selection.items
    } catch {
      return null
    }
  }, [])

  const describeRepair = useCallback((
    ids: string[],
    hydrated: Todo[],
    sent: TodoVersionReference[],
  ): BulkRepair => {
    const byId = new Map(hydrated.map((todo) => [todo.id, todo]))
    const vanished = ids.filter((id) => !byId.has(id))
    const drifted = sent
      .filter((reference) => {
        const current = byId.get(reference.id)
        return current !== undefined && current.version !== reference.version
      })
      .map((reference) => reference.id)
    const parts = [
      'The list changed while you were selecting.',
      drifted.length > 0 ? `${drifted.length} changed.` : '',
      vanished.length > 0 ? `${vanished.length} no longer exist.` : '',
      'Review and retry.',
    ].filter(Boolean)

    return {
      driftedIds: drifted,
      message: parts.join(' '),
      vanishedCount: vanished.length,
    }
  }, [])

  const attempt = useCallback(async (
    operation: BulkOperation,
    selection: TodoVersionReference[],
  ): Promise<BulkOutcome> => {
    try {
      return { kind: 'done', result: await send(operation, selection) }
    } catch (caught) {
      if (!(caught instanceof ApiError)) return { kind: 'failed', error: caught }

      // A domain rejection fails identically however often it is retried, so it
      // goes straight back to the caller with the server's own explanation.
      if (caught.kind === 'domain') return { kind: 'failed', error: caught }

      // A vanished identifier fails the batch as 404 before any version is
      // compared, so it reaches repair by the same route as a stale version.
      const repairable = caught.kind === 'concurrency' || caught.kind === 'not-found'
      if (!repairable) return { kind: 'failed', error: caught }

      const ids = selection.map((reference) => reference.id)
      const hydrated = await hydrate(ids)
      if (!hydrated) return { kind: 'failed', error: caught }

      return {
        kind: 'repair',
        repair: describeRepair(ids, hydrated, selection),
      }
    }
  }, [describeRepair, hydrate, send])

  /**
   * Retrying a shrunken selection would act on a subset the user never chose,
   * so a silent retry runs only when every identifier is still resolvable.
   */
  const run = useCallback(async (
    operation: BulkOperation,
    override?: TodoVersionReference[],
  ): Promise<BulkOutcome> => {
    const selection = override ?? buildSelection()
    if (selection.length === 0) return { kind: 'failed', error: null }

    setBulkBusy(true)
    setRepair(null)
    try {
      const first = await attempt(operation, selection)
      if (first.kind !== 'repair' || operation.kind === 'delete') {
        if (first.kind === 'repair') setRepair(first.repair)
        return first
      }

      const ids = selection.map((reference) => reference.id)
      const hydrated = await hydrate(ids)
      if (!hydrated || hydrated.length !== ids.length) {
        setRepair(first.repair)
        return first
      }

      const refreshed = hydrated.map((todo) => ({ id: todo.id, version: todo.version }))
      const second = await attempt(operation, refreshed)
      if (second.kind === 'repair') setRepair(second.repair)
      return second
    } finally {
      setBulkBusy(false)
    }
  }, [attempt, buildSelection, hydrate])

  const finish = useCallback((result: BulkTodoResult, sent: TodoVersionReference[]) => {
    const sentById = new Map(sent.map((reference) => [reference.id, reference.version]))
    const changed = result.items.filter(
      (item) => sentById.get(item.id) !== item.version,
    ).length
    const occurrences = result.items.filter((item) => item.nextOccurrenceId).length

    clearSelection()
    onRefresh()

    return { changed, occurrences, unchanged: result.items.length - changed }
  }, [clearSelection, onRefresh])

  return {
    buildSelection,
    bulkBusy,
    clearSelection,
    finish,
    overLimit,
    repair,
    run,
    selectLoaded,
    selectedCount,
    selectedIds,
    selectedStatuses,
    toggle,
  }
}
