import { describe, expect, it } from 'vitest'

import { readEventStream } from './sse.ts'

/**
 * The parser's job is to survive arbitrary chunking: a server writes whole
 * events, but a socket delivers whatever it happens to have. These tests own
 * the chunk boundaries, which is the one thing a browser test cannot do.
 */
function streamOf(...chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder()

  return new ReadableStream({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk))
      controller.close()
    },
  })
}

async function collect(stream: ReadableStream<Uint8Array>): Promise<string[]> {
  const payloads: string[] = []
  for await (const payload of readEventStream(stream)) payloads.push(payload)
  return payloads
}

describe('readEventStream', () => {
  it('reads several events delivered in one chunk', async () => {
    const payloads = await collect(streamOf(
      'event: turn_started\ndata: {"type":"turn_started"}\n\n'
      + 'event: message\ndata: {"type":"message"}\n\n',
    ))

    expect(payloads).toEqual(['{"type":"turn_started"}', '{"type":"message"}'])
  })

  it('reassembles an event split across chunks', async () => {
    const payloads = await collect(streamOf(
      'event: mes', 'sage\nda', 'ta: {"type":', '"message"}', '\n\n',
    ))

    expect(payloads).toEqual(['{"type":"message"}'])
  })

  /** The boundary itself can be split, which is the subtlest case. */
  it('reassembles an event whose terminator is split across chunks', async () => {
    const payloads = await collect(streamOf(
      'data: {"a":1}\n', '\ndata: {"b":2}\n\n',
    ))

    expect(payloads).toEqual(['{"a":1}', '{"b":2}'])
  })

  it('joins a payload spread over several data lines', async () => {
    const payloads = await collect(streamOf('data: {"a":\ndata: 1}\n\n'))

    expect(payloads).toEqual(['{"a":\n1}'])
  })

  it('ignores a record carrying no data, such as a comment', async () => {
    const payloads = await collect(streamOf(
      ': keep-alive\n\ndata: {"a":1}\n\n',
    ))

    expect(payloads).toEqual(['{"a":1}'])
  })

  /** A turn cut off mid-event yields what completed and nothing more. */
  it('drops a trailing record the stream never terminated', async () => {
    const payloads = await collect(streamOf(
      'data: {"a":1}\n\ndata: {"b":partial',
    ))

    expect(payloads).toEqual(['{"a":1}'])
  })

  it('survives a multi-byte character split across chunks', async () => {
    const encoder = new TextEncoder()
    const bytes = encoder.encode('data: {"text":"café"}\n\n')

    // Split inside the two-byte é, which a non-streaming decoder would mangle.
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes.slice(0, 18))
        controller.enqueue(bytes.slice(18))
        controller.close()
      },
    })

    expect(await collect(stream)).toEqual(['{"text":"café"}'])
  })

  /**
   * Leaving early has to release the body. Without the cancel, the connection
   * stays open, the server never sees the disconnect, and the turn keeps
   * running with the request scope it holds.
   */
  it('cancels the body when the reader leaves early', async () => {
    let cancelled = false
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode('data: {"a":1}\n\ndata: {"b":2}\n\n'))
      },
      cancel() {
        cancelled = true
      },
    })

    for await (const payload of readEventStream(stream)) {
      expect(payload).toBe('{"a":1}')
      break
    }

    expect(cancelled).toBe(true)
  })

  it('stops when the caller aborts', async () => {
    const controller = new AbortController()
    const stream = new ReadableStream<Uint8Array>({
      start(streamController) {
        streamController.enqueue(new TextEncoder().encode('data: {"a":1}\n\n'))
      },
    })

    const payloads: string[] = []
    for await (const payload of readEventStream(stream, controller.signal)) {
      payloads.push(payload)
      controller.abort()
    }

    expect(payloads).toEqual(['{"a":1}'])
  })
})
