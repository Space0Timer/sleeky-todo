import { send } from './http.ts'
import {
  dependencyStatus,
  sortDirection,
  todoPriority,
  todoScope,
  todoSortField,
  todoStatus,
  type BulkTodoResult,
  type CreateTodoDraft,
  type CursorPage,
  type Todo,
  type TodoDraft,
  type TodoListItem,
  type TodoListOptions,
  type TodoSelection,
  type TodoStatus,
  type TodoVersionReference,
} from '../types/todo.ts'

const statusNames = {
  [todoStatus.open]: 'Open',
  [todoStatus.inProgress]: 'InProgress',
  [todoStatus.completed]: 'Completed',
  [todoStatus.archived]: 'Archived',
}

const priorityNames = {
  [todoPriority.low]: 'Low',
  [todoPriority.medium]: 'Medium',
  [todoPriority.high]: 'High',
}

const scopeNames = {
  [todoScope.active]: 'Active',
  [todoScope.archived]: 'Archived',
  [todoScope.deleted]: 'Deleted',
}

const dependencyStatusNames = {
  [dependencyStatus.blocked]: 'Blocked',
  [dependencyStatus.unblocked]: 'Unblocked',
}

const sortFieldNames = {
  [todoSortField.dueDate]: 'DueDate',
  [todoSortField.priority]: 'Priority',
  [todoSortField.status]: 'Status',
  [todoSortField.name]: 'Name',
}

const directionNames = {
  [sortDirection.ascending]: 'Asc',
  [sortDirection.descending]: 'Desc',
}

export { ApiError } from './http.ts'

/**
 * Every TODO route is nested under the Space it belongs to, and every function
 * here takes that Space explicitly. There is no ambient "current Space" in this
 * module on purpose: the page reads it from the URL and passes it down, so a
 * request can never be sent against a Space the user is no longer looking at.
 */
function todosPath(spaceId: string, suffix = ''): string {
  return `/api/spaces/${encodeURIComponent(spaceId)}/todos${suffix}`
}

function todoPath(spaceId: string, todoId: string, suffix = ''): string {
  return todosPath(spaceId, `/${encodeURIComponent(todoId)}${suffix}`)
}

export function listTodos(
  spaceId: string,
  options: TodoListOptions,
): Promise<CursorPage<TodoListItem>> {
  const query = new URLSearchParams({
    scope: scopeNames[options.scope],
    sortField: sortFieldNames[options.sortField],
    sortDirection: directionNames[options.sortDirection],
    limit: String(options.limit),
  })

  if (options.status !== null) query.set('status', statusNames[options.status])
  if (options.priority !== null) query.set('priority', priorityNames[options.priority])
  if (options.dueFrom) query.set('due-from', options.dueFrom)
  if (options.dueTo) query.set('due-to', options.dueTo)
  if (options.dependencyStatus !== null) {
    query.set('dependencyStatus', dependencyStatusNames[options.dependencyStatus])
  }
  if (options.searchText) query.set('search', options.searchText)
  if (options.cursor) query.set('cursor', options.cursor)

  return send<CursorPage<TodoListItem>>(todosPath(spaceId, `?${query.toString()}`))
}

export function createTodo(spaceId: string, draft: CreateTodoDraft): Promise<Todo> {
  return send<Todo>(todosPath(spaceId), {
    method: 'POST',
    body: JSON.stringify(draft),
  })
}

export function getTodo(spaceId: string, id: string): Promise<Todo> {
  return send<Todo>(todoPath(spaceId, id))
}

export function updateTodo(spaceId: string, todo: Todo, draft: TodoDraft): Promise<Todo> {
  return send<Todo>(todoPath(spaceId, todo.id), {
    method: 'PUT',
    body: JSON.stringify({ ...draft, version: todo.version }),
  })
}

export function changeTodoStatus(
  spaceId: string,
  todo: Todo,
  status: Todo['status'],
): Promise<Todo> {
  return send<Todo>(todoPath(spaceId, todo.id, '/status'), {
    method: 'PUT',
    body: JSON.stringify({ status, version: todo.version }),
  })
}

export function addTodoDependency(
  spaceId: string,
  todo: Todo,
  dependencyId: string,
): Promise<Todo> {
  return send<Todo>(todoPath(spaceId, todo.id, '/dependencies'), {
    method: 'POST',
    body: JSON.stringify({ dependencyId, version: todo.version }),
  })
}

export function removeTodoDependency(
  spaceId: string,
  todo: Todo,
  dependencyId: string,
): Promise<Todo> {
  return send<Todo>(
    todoPath(spaceId, todo.id, `/dependencies/${encodeURIComponent(dependencyId)}`),
    {
      method: 'DELETE',
      body: JSON.stringify({ version: todo.version }),
    },
  )
}

export function deleteTodo(spaceId: string, todo: TodoVersionReference): Promise<Todo> {
  return send<Todo>(todoPath(spaceId, todo.id), {
    method: 'DELETE',
    body: JSON.stringify({ version: todo.version }),
  })
}

export function bulkChangeTodoStatus(
  spaceId: string,
  status: TodoStatus,
  items: TodoVersionReference[],
): Promise<BulkTodoResult> {
  // Enum names belong in query strings; a JSON body carries the numeric value,
  // which is what the single-item status route already sends.
  return send<BulkTodoResult>(todosPath(spaceId, '/status'), {
    method: 'PUT',
    body: JSON.stringify({ status, items }),
  })
}

export function bulkDeleteTodos(
  spaceId: string,
  items: TodoVersionReference[],
): Promise<BulkTodoResult> {
  return send<BulkTodoResult>(todosPath(spaceId), {
    method: 'DELETE',
    body: JSON.stringify({ items }),
  })
}

export function bulkRestoreTodos(
  spaceId: string,
  items: TodoVersionReference[],
): Promise<BulkTodoResult> {
  return send<BulkTodoResult>(todosPath(spaceId, '/restore'), {
    method: 'POST',
    body: JSON.stringify({ items }),
  })
}

/**
 * Reads the current state of exactly these TODOs without touching list state,
 * so a conflict path can diff a stale selection against the server without
 * refreshing what the user is looking at.
 */
export function lookupTodoSelection(spaceId: string, ids: string[]): Promise<TodoSelection> {
  const query = new URLSearchParams()
  for (const id of ids) query.append('id', id)

  return send<TodoSelection>(todosPath(spaceId, `/selection?${query.toString()}`))
}

export function restoreTodo(spaceId: string, todo: TodoVersionReference): Promise<Todo> {
  return send<Todo>(todoPath(spaceId, todo.id, '/restore'), {
    method: 'POST',
    body: JSON.stringify({ version: todo.version }),
  })
}
