import { useCallback, useRef, useState } from 'react'

import { runAssistantTurn } from '../api/assistant.ts'
import {
  turnEventType,
  type ChatEntry,
  type ConfirmationRequest,
  type ConfirmedAction,
  type TurnEvent,
} from '../types/assistant.ts'
import { type TodoVersionReference } from '../types/todo.ts'

type UseAssistantOptions = {
  onTodosChanged: () => void
}

/**
 * Holds one conversation and turns the event stream into what the panel shows.
 *
 * The transcript is opaque here. The server keeps no history, so each turn
 * hands one back and the next turn echoes it; nothing in the client reads it,
 * which is why its type is `unknown` rather than a shape to keep in step.
 */
export function useAssistant({ onTodosChanged }: UseAssistantOptions) {
  const [entries, setEntries] = useState<ChatEntry[]>([])
  const [pending, setPending] = useState(false)
  const [confirmation, setConfirmation] = useState<ConfirmationRequest | null>(null)
  const [error, setError] = useState<string | null>(null)
  const transcript = useRef<unknown>(undefined)

  const say = useCallback((entry: ChatEntry) => {
    setEntries((current) => [...current, entry])
  }, [])

  /**
   * A turn ends either with an answer or with a question the user has to
   * answer, so the confirmation is cleared when one starts and set only by the
   * event that raises it.
   */
  const consume = useCallback((event: TurnEvent) => {
    switch (event.type) {
      case turnEventType.message:
        say({ kind: 'assistant', text: event.data.text })
        break

      case turnEventType.toolExecuted:
        say({ kind: 'tool', summary: event.data.summary })
        break

      case turnEventType.todosChanged:
        onTodosChanged()
        break

      case turnEventType.confirmationRequired:
        setConfirmation(event.data)
        break

      case turnEventType.turnCompleted:
        transcript.current = event.data.messages
        break

      default:
        // turn_started is a marker, and heartbeat is transport.
        break
    }
  }, [onTodosChanged, say])

  const run = useCallback(async (
    message: string | null,
    confirmed: ConfirmedAction | null,
  ) => {
    setPending(true)
    setError(null)
    setConfirmation(null)

    try {
      const stream = runAssistantTurn({
        message,
        transcript: transcript.current,
        confirmation: confirmed,
      })

      for await (const event of stream) {
        consume(event)
      }
    } catch (caught) {
      setError(caught instanceof Error
        ? caught.message
        : 'The assistant stopped unexpectedly.')
    } finally {
      setPending(false)
    }
  }, [consume])

  const ask = useCallback(async (message: string) => {
    say({ kind: 'user', text: message })
    await run(message, null)
  }, [run, say])

  /**
   * Confirms with the versions the proposal displayed rather than re-reading.
   * That is what makes a repeated confirmation fail on the moved version
   * instead of deleting whatever has since taken their place.
   */
  const confirm = useCallback(async (
    tool: string,
    items: TodoVersionReference[],
  ) => {
    say({ kind: 'user', text: 'Confirmed.' })
    await run(null, { tool, items })
  }, [run, say])

  const cancel = useCallback(() => {
    setConfirmation(null)
    say({ kind: 'user', text: 'Cancelled.' })
  }, [say])

  const reset = useCallback(() => {
    transcript.current = undefined
    setEntries([])
    setConfirmation(null)
    setError(null)
  }, [])

  return { ask, cancel, confirm, confirmation, entries, error, pending, reset }
}
