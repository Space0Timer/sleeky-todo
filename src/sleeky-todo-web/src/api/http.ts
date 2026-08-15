import type { ApiErrorKind, ProblemDetails } from '../types/todo.ts'

export type AntiforgeryToken = {
  /**
   * The field name a form post must use. Validation reads the form field
   * before the header when a request has a form content type, which is what
   * lets the sign-out navigation stay protected — a browser-owned form post
   * cannot set a request header.
   */
  formFieldName: string
  headerName: string
  token: string
}

const defaultAntiforgeryHeader = 'X-CSRF-TOKEN'
const mutatingMethods = new Set(['DELETE', 'PATCH', 'POST', 'PUT'])

/**
 * Antiforgery tokens are bound to the authenticated identity, so the token is
 * kept in memory only and replaced whenever authentication state changes.
 */
let antiforgeryToken: AntiforgeryToken | null = null

export function setAntiforgeryToken(next: AntiforgeryToken | null): void {
  antiforgeryToken = next
}

function classifyError(status: number, problem: ProblemDetails): ApiErrorKind {
  if (status === 0) return 'network'
  if (status === 400) return 'validation'
  if (status === 401 || status === 403) return 'unauthorized'
  if (status === 404) return 'not-found'
  if (status === 409 && problem.title === 'Concurrency conflict.') return 'concurrency'
  if (status === 409) return 'domain'
  if (status === 429) return 'rate-limited'
  return 'unexpected'
}

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly problem: ProblemDetails
  readonly status: number

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}.`)
    this.name = 'ApiError'
    this.problem = problem
    this.status = status
    this.kind = classifyError(status, problem)
  }
}

/**
 * Exported because a streamed response cannot go through `send`, which reads
 * the whole body as JSON. The antiforgery rule is the same either way, so the
 * header is built here rather than assembled a second time at that call site.
 */
export function buildHeaders(init?: RequestInit): HeadersInit {
  const method = (init?.method ?? 'GET').toUpperCase()
  const needsToken = mutatingMethods.has(method) && antiforgeryToken !== null

  const headers = new Headers({ Accept: 'application/json' })

  if (init?.body) {
    headers.set('Content-Type', 'application/json')
  }

  // Caller headers are applied before the antiforgery header so a caller
  // cannot drop cross-site request protection by supplying its own headers.
  // They are merged through a Headers instance because RequestInit also
  // accepts tuple arrays and Headers, and spreading either of those into an
  // object produces numeric indices rather than header names.
  new Headers(init?.headers).forEach((value, name) => headers.set(name, value))

  if (needsToken) {
    headers.set(
      antiforgeryToken?.headerName || defaultAntiforgeryHeader,
      antiforgeryToken?.token ?? '',
    )
  }

  return headers
}

export async function send<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response

  try {
    response = await fetch(path, {
      ...init,
      credentials: 'include',
      headers: buildHeaders(init),
    })
  } catch {
    throw new ApiError(0, {
      title: 'Unable to reach the API.',
      detail: 'Check that the API and MongoDB are running.',
    })
  }

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
