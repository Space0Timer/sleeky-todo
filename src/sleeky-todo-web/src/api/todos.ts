import type {
  ProblemDetails,
  Todo,
  TodoDraft,
} from '../types/todo.ts'

export class ApiError extends Error {
  readonly problem: ProblemDetails
  readonly status: number

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}.`)
    this.name = 'ApiError'
    this.problem = problem
    this.status = status
  }
}

async function send<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })

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

export function createTodo(draft: TodoDraft): Promise<Todo> {
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

export function deleteTodo(todo: Todo): Promise<void> {
  return send<void>(`/api/todos/${encodeURIComponent(todo.id)}`, {
    method: 'DELETE',
    body: JSON.stringify({ version: todo.version }),
  })
}

export function restoreTodo(todo: Todo): Promise<Todo> {
  return send<Todo>(`/api/todos/${encodeURIComponent(todo.id)}/restore`, {
    method: 'POST',
    body: JSON.stringify({ version: todo.version }),
  })
}
