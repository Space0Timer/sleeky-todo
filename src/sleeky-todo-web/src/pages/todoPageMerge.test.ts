import { describe, expect, it } from 'vitest'

import { todoPriority, todoStatus, type TodoListItem } from '../types/todo.ts'
import { mergeTodoPage } from './todoPageMerge.ts'

/**
 * These cases own the one thing a browser test cannot stage reliably: a write
 * that lands between two page fetches, in a chosen direction, against a list
 * the user is halfway through reading.
 */
function todoOf(
  id: string,
  version: number,
  overrides: Partial<TodoListItem> = {},
): TodoListItem {
  return {
    id,
    name: id,
    descriptionPreview: null,
    dueDate: '2026-08-15',
    status: todoStatus.notStarted,
    priority: todoPriority.medium,
    isRecurring: false,
    isBlocked: false,
    incompleteDependencyCount: 0,
    version,
    deletedAt: null,
    purgeAt: null,
    ...overrides,
  }
}

function idsOf(items: TodoListItem[]): string[] {
  return items.map((item) => item.id)
}

describe('mergeTodoPage', () => {
  it('appends a page that shares nothing with the list', () => {
    const merged = mergeTodoPage(
      [todoOf('a', 1), todoOf('b', 1)],
      [todoOf('c', 1), todoOf('d', 1)],
    )

    expect(idsOf(merged)).toEqual(['a', 'b', 'c', 'd'])
  })

  it('keeps one copy of a TODO the next page repeats', () => {
    const merged = mergeTodoPage(
      [todoOf('a', 1), todoOf('b', 1)],
      [todoOf('b', 2), todoOf('c', 1)],
    )

    expect(idsOf(merged)).toEqual(['a', 'b', 'c'])
  })

  /**
   * The repeat is the fresher read, so its due date is the one the user is
   * owed: the copy on screen predates the edit that moved the row.
   */
  it('takes the newer version of a repeated TODO', () => {
    const merged = mergeTodoPage(
      [todoOf('a', 3, { dueDate: '2026-08-01' })],
      [todoOf('a', 4, { dueDate: '2026-09-01' })],
    )

    expect(merged).toEqual([todoOf('a', 4, { dueDate: '2026-09-01' })])
  })

  /**
   * A page can be served a read that trails what the list already holds, and
   * an older copy overwriting a newer one would undo an edit on screen.
   */
  it('keeps the version on screen when the page trails it', () => {
    const merged = mergeTodoPage(
      [todoOf('a', 5, { dueDate: '2026-09-01' })],
      [todoOf('a', 4, { dueDate: '2026-08-01' })],
    )

    expect(merged).toEqual([todoOf('a', 5, { dueDate: '2026-09-01' })])
  })

  it('leaves a repeated TODO where the user last saw it', () => {
    const merged = mergeTodoPage(
      [todoOf('a', 1), todoOf('b', 1), todoOf('c', 1)],
      [todoOf('d', 1), todoOf('b', 2)],
    )

    expect(idsOf(merged)).toEqual(['a', 'b', 'c', 'd'])
  })

  it('collapses a TODO repeated within one page', () => {
    const merged = mergeTodoPage(
      [],
      [todoOf('a', 1), todoOf('b', 1), todoOf('a', 2)],
    )

    expect(idsOf(merged)).toEqual(['a', 'b'])
    expect(merged[0].version).toBe(2)
  })

  it('returns the list unchanged for an empty page', () => {
    const merged = mergeTodoPage([todoOf('a', 1)], [])

    expect(merged).toEqual([todoOf('a', 1)])
  })
})
