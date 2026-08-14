import {
  type AssistantProbeResult,
  type AssistantSettings,
  type AssistantSettingsDraft,
  type AssistantTurnRequest,
  type TurnEvent,
} from '../types/assistant.ts'
import { ApiError, buildHeaders, send } from './http.ts'
import { readEventStream } from './sse.ts'

const turnsPath = '/api/assistant/turns'
const settingsPath = '/api/assistant/settings'

/**
 * Runs one turn, yielding what it reports as it happens.
 *
 * The transcript is echoed back from the previous turn and never inspected
 * here: the server keeps no conversation history, and the client is only its
 * courier.
 */
export async function* runAssistantTurn(
  request: AssistantTurnRequest,
  signal?: AbortSignal,
): AsyncGenerator<TurnEvent, void, undefined> {
  const body = JSON.stringify(request)
  let response: Response

  try {
    response = await fetch(turnsPath, {
      method: 'POST',
      credentials: 'include',
      headers: buildHeaders({
        method: 'POST',
        body,
        headers: { Accept: 'text/event-stream' },
      }),
      body,
      signal,
    })
  } catch {
    throw new ApiError(0, {
      title: 'Unable to reach the assistant.',
      detail: 'Check that the API is running.',
    })
  }

  if (!response.ok || response.body === null) {
    throw await readProblem(response)
  }

  for await (const payload of readEventStream(response.body, signal)) {
    const event = parseEvent(payload)
    if (event !== null) yield event
  }
}

export function getAssistantSettings(): Promise<AssistantSettings> {
  return send<AssistantSettings>(settingsPath)
}

export function saveAssistantSettings(
  draft: AssistantSettingsDraft,
): Promise<AssistantSettings> {
  // An empty key field is "leave the stored one alone", not "clear it": the
  // user cannot read their key back to retype it alongside a model change.
  return send<AssistantSettings>(settingsPath, {
    method: 'PUT',
    body: JSON.stringify({
      provider: draft.provider,
      baseUrl: draft.baseUrl.trim() === '' ? null : draft.baseUrl.trim(),
      model: draft.model,
      apiKey: draft.apiKey === '' ? null : draft.apiKey,
    }),
  })
}

export function deleteAssistantSettings(): Promise<void> {
  return send<void>(settingsPath, { method: 'DELETE' })
}

/**
 * Probes the values on the form rather than the stored ones, so a key the user
 * has just typed is what gets checked. Nothing sent here is persisted.
 */
export function testAssistantConnection(
  draft: AssistantSettingsDraft,
): Promise<AssistantProbeResult> {
  return send<AssistantProbeResult>(`${settingsPath}/test`, {
    method: 'POST',
    body: JSON.stringify({
      provider: draft.provider,
      baseUrl: draft.baseUrl.trim() === '' ? null : draft.baseUrl.trim(),
      model: draft.model,
      apiKey: draft.apiKey === '' ? null : draft.apiKey,
    }),
  })
}

/**
 * An unreadable event is dropped rather than failing the turn. The stream is
 * advisory — every write it reports has already committed — so the useful
 * response to one bad record is to keep reading.
 */
function parseEvent(payload: string): TurnEvent | null {
  try {
    const parsed: unknown = JSON.parse(payload)

    if (
      typeof parsed === 'object'
      && parsed !== null
      && typeof (parsed as { type?: unknown }).type === 'string'
    ) {
      return parsed as TurnEvent
    }
  } catch {
    return null
  }

  return null
}

async function readProblem(response: Response): Promise<ApiError> {
  try {
    return new ApiError(response.status, await response.json())
  } catch {
    return new ApiError(response.status, {
      detail: `The assistant returned status ${response.status}.`,
    })
  }
}
