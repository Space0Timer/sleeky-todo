export const todoStatus = {
  notStarted: 0,
  inProgress: 1,
  completed: 2,
  archived: 3,
} as const

export type TodoStatus = (typeof todoStatus)[keyof typeof todoStatus]

export const todoStatusLabels: Record<TodoStatus, string> = {
  [todoStatus.notStarted]: 'Not started',
  [todoStatus.inProgress]: 'In progress',
  [todoStatus.completed]: 'Completed',
  [todoStatus.archived]: 'Archived',
}

export const todoPriority = {
  low: 0,
  medium: 1,
  high: 2,
} as const

export type TodoPriority = (typeof todoPriority)[keyof typeof todoPriority]

export const todoPriorityLabels: Record<TodoPriority, string> = {
  [todoPriority.low]: 'Low',
  [todoPriority.medium]: 'Medium',
  [todoPriority.high]: 'High',
}

export const recurrenceType = {
  daily: 0,
  weekly: 1,
  monthly: 2,
  custom: 3,
} as const

export type RecurrenceType = (typeof recurrenceType)[keyof typeof recurrenceType]

export const recurrenceUnit = {
  days: 0,
  weeks: 1,
  months: 2,
} as const

export type RecurrenceUnit = (typeof recurrenceUnit)[keyof typeof recurrenceUnit]

export type RecurrenceSchedule = {
  type: RecurrenceType
  interval: number
  unit: RecurrenceUnit
  anchorDay: number | null
}

export type RecurrenceDraft = {
  type: RecurrenceType
  interval: number
  unit: RecurrenceUnit | null
}

export type TodoDraft = {
  name: string
  description: string
  dueDate: string
  priority: TodoPriority
}

export type CreateTodoDraft = TodoDraft & {
  recurrence: RecurrenceDraft | null
}

export type Todo = {
  id: string
  name: string
  description: string | null
  dueDate: string
  status: TodoStatus
  priority: TodoPriority
  dependencyIds: string[]
  recurrence: RecurrenceSchedule | null
  seriesId: string | null
  occurrenceNumber: number | null
  version: number
  createdAt: string
  updatedAt: string
  deletedAt: string | null
  purgeAt: string | null
  nextOccurrenceId: string | null
}

export type TodoListItem = {
  id: string
  name: string
  descriptionPreview: string | null
  dueDate: string
  status: TodoStatus
  priority: TodoPriority
  isRecurring: boolean
  isBlocked: boolean
  incompleteDependencyCount: number
  version: number
  deletedAt: string | null
  purgeAt: string | null
}

export type CursorPage<T> = {
  items: T[]
  nextCursor: string | null
}

/**
 * The outcome of one selected TODO. `version` is unchanged for an item that
 * needed no write, so comparing it with the version sent distinguishes a real
 * transition from a no-op.
 */
export type BulkTodoResultItem = {
  id: string
  version: number
  status: TodoStatus
  deletedAt: string | null
  nextOccurrenceId: string | null
}

export type BulkTodoResult = {
  items: BulkTodoResultItem[]
}

/** Found-only: identifiers that no longer resolve are absent, not reported. */
export type TodoSelection = {
  items: Todo[]
}

/** A batch applies in full or not at all, and the server caps it at this size. */
export const maximumBulkSelection = 100

export const todoScope = {
  active: 0,
  archived: 1,
  deleted: 2,
} as const

export type TodoScope = (typeof todoScope)[keyof typeof todoScope]

export const dependencyStatus = {
  blocked: 0,
  unblocked: 1,
} as const

export type DependencyStatus = (typeof dependencyStatus)[keyof typeof dependencyStatus]

export const todoSortField = {
  dueDate: 0,
  priority: 1,
  status: 2,
  name: 3,
} as const

export type TodoSortField = (typeof todoSortField)[keyof typeof todoSortField]

export const sortDirection = {
  ascending: 0,
  descending: 1,
} as const

export type SortDirection = (typeof sortDirection)[keyof typeof sortDirection]

export type TodoListOptions = {
  scope: TodoScope
  status: TodoStatus | null
  priority: TodoPriority | null
  dueFrom: string
  dueTo: string
  dependencyStatus: DependencyStatus | null
  /** Words to match, `''` for no search. Required so a caller cannot forget it. */
  searchText: string
  sortField: TodoSortField
  sortDirection: SortDirection
  limit: number
  cursor?: string | null
}

export type TodoVersionReference = Pick<Todo, 'id' | 'version'>

export type ProblemDetails = {
  title?: string
  status?: number
  detail?: string
  traceId?: string
  errors?: Record<string, string[]>
}

export type ApiErrorKind =
  | 'validation'
  | 'not-found'
  | 'domain'
  | 'concurrency'
  | 'network'
  | 'rate-limited'
  | 'unauthorized'
  | 'unexpected'
