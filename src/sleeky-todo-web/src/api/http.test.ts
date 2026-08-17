import { describe, expect, it } from 'vitest'

import { ApiError, classifyError } from './http.ts'

/**
 * The split that matters is 401 against 403. Both used to end the session;
 * now only a missing session does, and a member acting above their permission
 * in a Space is told so and stays signed in.
 */
describe('classifyError', () => {
  it('treats a missing session as unauthorized', () => {
    expect(classifyError(401, { title: 'Unauthorized' })).toBe('unauthorized')
  })

  it('treats a live session acting above its permission as forbidden', () => {
    expect(classifyError(403, { title: 'Forbidden.' })).toBe('forbidden')
  })

  it('tells a concurrency conflict from a domain rejection by title', () => {
    expect(classifyError(409, { title: 'Concurrency conflict.' })).toBe('concurrency')
    expect(classifyError(409, { title: 'Business rule violation.' })).toBe('domain')
  })

  it('maps the remaining statuses the page reacts to', () => {
    expect(classifyError(0, {})).toBe('network')
    expect(classifyError(400, {})).toBe('validation')
    expect(classifyError(404, {})).toBe('not-found')
    expect(classifyError(429, {})).toBe('rate-limited')
    expect(classifyError(500, {})).toBe('unexpected')
  })

  it('is what ApiError reads its kind from', () => {
    const forbidden = new ApiError(403, { title: 'Forbidden.', detail: 'Write required.' })
    const expired = new ApiError(401, {})

    expect(forbidden.kind).toBe('forbidden')
    expect(forbidden.message).toBe('Write required.')
    expect(expired.kind).toBe('unauthorized')
  })
})
