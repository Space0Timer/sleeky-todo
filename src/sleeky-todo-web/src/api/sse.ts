/**
 * Reads a server-sent event stream off a fetch response body.
 *
 * `EventSource` is not usable here: antiforgery is a global requirement on this
 * API and `EventSource` can only issue a GET, so the turn is a POST whose body
 * is parsed by hand. That also means no automatic reconnection, which is the
 * behaviour we want — a dropped turn loses nothing, because a tool call that
 * committed stays committed, and silently replaying one would not be safe.
 */
export async function* readEventStream(
  body: ReadableStream<Uint8Array>,
  signal?: AbortSignal,
): AsyncGenerator<string, void, undefined> {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  let buffered = ''

  const abort = () => void reader.cancel().catch(() => {})
  signal?.addEventListener('abort', abort)

  try {
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break

      // Events arrive split across reads at arbitrary points, so a chunk is
      // appended and only whole records are taken off the front.
      buffered += decoder.decode(value, { stream: true })

      let boundary = buffered.indexOf('\n\n')
      while (boundary !== -1) {
        const record = buffered.slice(0, boundary)
        buffered = buffered.slice(boundary + 2)

        const payload = readData(record)
        if (payload !== null) yield payload

        boundary = buffered.indexOf('\n\n')
      }
    }
  } finally {
    signal?.removeEventListener('abort', abort)

    // Cancelled before the lock is released. A consumer that stopped early —
    // by breaking out, or by throwing from the yield — otherwise leaves the
    // response body open, so the server never sees the disconnect and keeps
    // the turn, its request scope, and its provider call alive. Cancelling a
    // stream that already ended is a no-op.
    await reader.cancel().catch(() => {})
    reader.releaseLock()
  }
}

/**
 * Takes the `data:` lines from one record. The event name is ignored: every
 * payload names its own type, so the reducer switches on one thing rather than
 * on two that could disagree.
 */
function readData(record: string): string | null {
  const data = record
    .split('\n')
    .filter((line) => line.startsWith('data:'))
    .map((line) => line.slice('data:'.length).trimStart())

  return data.length === 0 ? null : data.join('\n')
}
