export const todoPriority = {
  low: 0,
  medium: 1,
  high: 2,
} as const

export type TodoPriority = (typeof todoPriority)[keyof typeof todoPriority]

export type TodoDraft = {
  name: string
  description: string
  dueDate: string
  priority: TodoPriority
}

export type Todo = {
  id: string
  name: string
  description: string | null
  dueDate: string
  status: number
  priority: TodoPriority
  dependencyIds: string[]
  seriesId: string | null
  occurrenceNumber: number | null
  version: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  purgeAt: string | null
}

export type ProblemDetails = {
  title?: string
  status?: number
  detail?: string
  traceId?: string
  errors?: Record<string, string[]>
}
