import { createServer } from 'node:http'

/**
 * An OpenAI-compatible endpoint whose every reply the test writes.
 *
 * The assistant reaches self-hosted models through a configurable base URL, so
 * pointing it here needs no production seam and no test-only branch: the turn
 * runs through exactly the client, loop, and tool layer a real provider would.
 * That is what lets the browser suite cover the assistant's actual behaviour —
 * a confirmation dialog, a list refreshing after a write — rather than only the
 * paths that need no model.
 *
 * `POST /__script` sets the replies for the next turn, newest script winning.
 * Each entry is either `{ text }` or `{ tool, arguments }`, and one is consumed
 * per round of the function-calling loop.
 */
const port = Number(process.env.STUB_PROVIDER_PORT ?? 4599)
let replies = []

function toChoice(reply) {
  if (reply?.tool) {
    return {
      index: 0,
      finish_reason: 'tool_calls',
      message: {
        role: 'assistant',
        content: null,
        tool_calls: [{
          id: `call_${Math.random().toString(36).slice(2, 10)}`,
          type: 'function',
          function: {
            name: reply.tool,
            arguments: JSON.stringify(reply.arguments ?? {}),
          },
        }],
      },
    }
  }

  return {
    index: 0,
    finish_reason: 'stop',
    message: { role: 'assistant', content: reply?.text ?? 'Anything else?' },
  }
}

function readBody(request) {
  return new Promise((resolve) => {
    let body = ''
    request.on('data', (chunk) => (body += chunk))
    request.on('end', () => resolve(body))
  })
}

const server = createServer(async (request, response) => {
  const url = request.url ?? ''

  if (url.startsWith('/__health')) {
    response.writeHead(200).end('ok')
    return
  }

  if (url.startsWith('/__script')) {
    replies = JSON.parse((await readBody(request)) || '[]')
    response.writeHead(204).end()
    return
  }

  if (!url.startsWith('/chat/completions')) {
    response.writeHead(404).end()
    return
  }

  await readBody(request)

  response.writeHead(200, { 'content-type': 'application/json' })
  response.end(JSON.stringify({
    id: 'chatcmpl-stub',
    object: 'chat.completion',
    created: Math.floor(Date.now() / 1000),
    model: 'stub',
    choices: [toChoice(replies.shift())],
    usage: { prompt_tokens: 1, completion_tokens: 1, total_tokens: 2 },
  }))
})

server.listen(port, '127.0.0.1', () => {
  process.stdout.write(`stub provider listening on ${port}\n`)
})
