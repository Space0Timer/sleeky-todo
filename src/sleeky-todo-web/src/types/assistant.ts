import { type TodoStatus, type TodoVersionReference } from './todo.ts'

/**
 * The coarse steps a turn reports. `heartbeat` is transport — it keeps an idle
 * stream from timing out while the model thinks — and `turn_completed` carries
 * the conversation forward, because the server stores no history.
 */
export const turnEventType = {
  turnStarted: 'turn_started',
  toolExecuted: 'tool_executed',
  confirmationRequired: 'confirmation_required',
  todosChanged: 'todos_changed',
  message: 'message',
  turnCompleted: 'turn_completed',
  heartbeat: 'heartbeat',
} as const

export type TurnEventType = (typeof turnEventType)[keyof typeof turnEventType]

export type ToolExecution = {
  tool: string
  summary: string
  succeeded: boolean
}

export type AssistantMessage = {
  text: string
}

export type TodoChangeNotice = {
  ids: string[]
}

export type ConfirmationItem = {
  id: string
  name: string
  version: number
  status: TodoStatus
  deletedAt: string | null
}

export type ConfirmationRequest = {
  tool: string
  prompt: string
  items: ConfirmationItem[]
}

export type TurnTranscript = {
  messages: unknown
}

export type TurnEvent =
  | { type: 'turn_started'; data: null }
  | { type: 'tool_executed'; data: ToolExecution }
  | { type: 'confirmation_required'; data: ConfirmationRequest }
  | { type: 'todos_changed'; data: TodoChangeNotice }
  | { type: 'message'; data: AssistantMessage }
  | { type: 'turn_completed'; data: TurnTranscript }
  | { type: 'heartbeat'; data: null }

export type ConfirmedAction = {
  tool: string
  items: TodoVersionReference[]
}

export type AssistantTurnRequest = {
  message?: string | null
  transcript?: unknown
  confirmation?: ConfirmedAction | null
}

export const assistantProvider = {
  anthropic: 'Anthropic',
  openAiCompatible: 'OpenAiCompatible',
} as const

export type AssistantProvider =
  (typeof assistantProvider)[keyof typeof assistantProvider]

export const assistantProviderLabels: Record<AssistantProvider, string> = {
  [assistantProvider.anthropic]: 'Anthropic',
  [assistantProvider.openAiCompatible]: 'OpenAI-compatible',
}

/**
 * There is no key on this type, and no endpoint returns one. `hasKey` says a
 * key is stored; `isUsable` says the whole configuration resolves.
 */
export type AssistantSettings = {
  provider: string
  baseUrl: string | null
  model: string
  hasKey: boolean
  isUsable: boolean
  source: string
}

export type AssistantSettingsDraft = {
  provider: AssistantProvider
  baseUrl: string
  model: string
  apiKey: string
}

export type AssistantProbeResult = {
  succeeded: boolean
  error: string | null
}

/**
 * What the panel shows: the user's own turns and the assistant's replies.
 *
 * A user entry carries `delivered`, because a turn that fails leaves the
 * transcript where it was: the message is on screen but the assistant has no
 * record of it, and saying so is more honest than showing it as sent.
 */
export type ChatEntry =
  | { kind: 'user'; text: string; delivered: boolean }
  | { kind: 'assistant'; text: string }
  | { kind: 'tool'; summary: string }
