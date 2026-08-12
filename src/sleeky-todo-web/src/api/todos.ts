import {
  dependencyStatus,
  sortDirection,
  todoPriority,
  todoScope,
  todoSortField,
  todoStatus,
  type ApiErrorKind,
  type CreateTodoDraft,
  type CursorPage,
  type ProblemDetails,
  type Todo,
  type TodoDraft,
  type TodoListItem,
  type TodoListOptions,
  type TodoVersionReference,
} from '../types/todo.ts'

const statusNames = {
  [todoStatus.notStarted]: 'NotStarted',
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

function classifyError(status: number, problem: ProblemDetails): ApiErrorKind {
  if (status === 0) return 'network'
  if (status === 400) return 'validation'
  if (status === 404) return 'not-found'
  if (status === 409 && problem.title === 'Concurrency conflict.') return 'concurrency'
  if (status === 409) return 'domain'
  return 'unexpected'
}

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly problem: ProblemDetails
  readonly status: number

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}.`)
    this.name = 'ApiError'
    this.problem = problem
    this.status = status
    this.kind = classifyError(status, problem)
  }
}

async function send<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response

  try {
    response = await fetch(path, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...init?.headers,
      },
    })
  } catch {
    throw new ApiError(0, {
      title: 'Unable to reach the API.',
      detail: 'Check that the API and MongoDB are running.',
    })
  }

  if (!response.ok) {
    let problem: ProblemDetails = {}

    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      problem = { detail: `Request failed with status ${response.status}.` }
    }

    throw new ApiError(response.status, problem)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export function listTodos(options: TodoListOptions): Promise<CursorPage<TodoListItem>> {
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
  if (options.cursor) query.set('cursor', options.cursor)

  return send<CursorPage<TodoListItem>>(`/api/todos?${query.toString()}`)
}

export function createTodo(draft: CreateTodoDraft): Promise<Todo> {
  return send<Todo>('/api/todos', {
    method: 'POST',
    body: JSON.stringify(draft),
  })
}

export function getTodo(id: string): Promise<Todo> {
  return send<Todo>(`/api/todos/${encodeURIComponent(id)}`)
}

export function updateTodo(todo: Todo, draft: TodoDraft): Promise<Todo> {
  return send<Todo>(`/api/todos/${encodeURIComponent(todo.id)}`, {
    method: 'PUT',
    body: JSON.stringify({ ...draft, version: todo.version }),
  })
}

export function changeTodoStatus(todo: Todo, status: Todo['status']): Promise<Todo> {
  return send<Todo>(`/api/todos/${encodeURIComponent(todo.id)}/status`, {
    method: 'PUT',
    body: JSON.stringify({ status, version: todo.version }),
  })
}

export function addTodoDependency(todo: Todo, dependencyId: string): Promise<Todo> {
  return send<Todo>(`/api/todos/${encodeURIComponent(todo.id)}/dependencies`, {
    method: 'POST',
    body: JSON.stringify({ dependencyId, version: todo.version }),
  })
}

export function removeTodoDependency(todo: Todo, dependencyId: string): Promise<Todo> {
  return send<Todo>(
    `/api/todos/${encodeURIComponent(todo.id)}/dependencies/${encodeURIComponent(dependencyId)}`,
    {
      method: 'DELETE',
      body: JSON.stringify({ version: todo.version }),
    },
  )
}

export function deleteTodo(todo: TodoVersionReference): Promise<void> {
  return send<void>(`/api/todos/${encodeURIComponent(todo.id)}`, {
    method: 'DELETE',
    body: JSON.stringify({ version: todo.version }),
  })
}

export function restoreTodo(todo: TodoVersionReference): Promise<Todo> {
  return send<Todo>(`/api/todos/${encodeURIComponent(todo.id)}/restore`, {
    method: 'POST',
    body: JSON.stringify({ version: todo.version }),
  })
}
