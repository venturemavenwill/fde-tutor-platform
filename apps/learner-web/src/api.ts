import { getAuthContext, type AuthContext } from './auth'
import type {
  AccessConsole,
  ApiFailure,
  CommandAccepted,
  Criterion,
  CriterionReveal,
  LearningHome,
  ObservedUser,
  S083Content,
  SessionState,
  SourceOpen,
} from './contracts'

const apiBase = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

export class ApiError extends Error {
  readonly status: number
  readonly details?: ApiFailure

  constructor(
    message: string,
    status: number,
    details?: ApiFailure,
  ) {
    super(message)
    this.status = status
    this.details = details
  }
}

async function request<T>(
  path: string,
  init: RequestInit = {},
  idempotencyKey?: string,
  providedAuth?: AuthContext,
): Promise<T> {
  const auth = providedAuth ?? (await getAuthContext())
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  Object.entries(auth.headers).forEach(([name, value]) => headers.set(name, value))
  if (init.body) {
    headers.set('Content-Type', 'application/json')
  }
  if (idempotencyKey) {
    headers.set('Idempotency-Key', idempotencyKey)
    headers.set('X-Correlation-Id', crypto.randomUUID())
  }

  let response: Response
  try {
    response = await fetch(`${apiBase}${path}`, { ...init, headers })
  } catch (cause) {
    if (cause instanceof TypeError) {
      throw new ApiError(
        `The platform API at ${apiBase} could not be reached. For local review, start the app with launch-fde-tutor.cmd and keep its window open.`,
        0,
      )
    }
    throw cause
  }
  if (!response.ok) {
    const details = (await response.json().catch(() => undefined)) as
      | ApiFailure
      | undefined
    throw new ApiError(
      details?.message ?? `The platform request failed with status ${response.status}.`,
      response.status,
      details,
    )
  }
  return (await response.json()) as T
}

async function command<T>(
  path: string,
  payload: unknown,
): Promise<T> {
  const auth = await getAuthContext()
  const body = JSON.stringify(payload)
  const fingerprintBytes = await crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(`${path}\0${body}`),
  )
  const fingerprint = Array.from(new Uint8Array(fingerprintBytes), (byte) =>
    byte.toString(16).padStart(2, '0'),
  ).join('')
  const identitySegment = encodeURIComponent(auth.identityKey)
  const prefix = 'fde-tutor:idempotency:'
  for (let index = localStorage.length - 1; index >= 0; index -= 1) {
    const key = localStorage.key(index)
    if (
      key?.startsWith(prefix) &&
      !key.startsWith(`${prefix}${identitySegment}:`)
    ) {
      localStorage.removeItem(key)
    }
  }
  const storageKey = `${prefix}${identitySegment}:${path}`
  const stored = localStorage.getItem(storageKey)
  let idempotencyKey: string | undefined
  if (stored) {
    try {
      const entry = JSON.parse(stored) as {
        fingerprint?: string
        idempotencyKey?: string
      }
      if (entry.fingerprint === fingerprint && entry.idempotencyKey) {
        idempotencyKey = entry.idempotencyKey
      }
    } catch {
      localStorage.removeItem(storageKey)
    }
  }
  idempotencyKey ??= crypto.randomUUID()
  localStorage.setItem(
    storageKey,
    JSON.stringify({ fingerprint, idempotencyKey }),
  )

  const result = await request<T>(
    path,
    {
      method: 'POST',
      body,
    },
    idempotencyKey,
    auth,
  )
  const current = localStorage.getItem(storageKey)
  if (current?.includes(idempotencyKey)) {
    localStorage.removeItem(storageKey)
  }
  return result
}

export const api = {
  getAccess: () => request<AccessConsole>('/api/v1/access'),
  getObservedUsers: () => request<ObservedUser[]>('/api/v1/access/users'),
  getContent: () => request<S083Content>('/api/v1/s083/content'),
  getHome: () => request<LearningHome>('/api/v1/s083/learning-home'),
  getSession: (sessionId: string) =>
    request<SessionState>(`/api/v1/s083/sessions/${sessionId}`),
  startSession: (contentRevision: string) =>
    command<CommandAccepted>('/api/v1/s083/sessions', { contentRevision }),
  recordText: (
    sessionId: string,
    action:
      | 'expectation'
      | 'unpaid-remedy'
      | 'comparison'
      | 'revision'
      | 'authentic-transfer'
      | 'retrieval',
    contentRevision: string,
    response: string,
  ) =>
    command<CommandAccepted>(`/api/v1/s083/sessions/${sessionId}/${action}`, {
      contentRevision,
      response,
    }),
  recordResponses: (
    sessionId: string,
    action: 'cold-start' | 'priming',
    contentRevision: string,
    responses: Record<string, string>,
  ) =>
    command<CommandAccepted>(`/api/v1/s083/sessions/${sessionId}/${action}`, {
      contentRevision,
      responses,
    }),
  revealCriterion: (sessionId: string, contentRevision: string) =>
    command<CriterionReveal>(
      `/api/v1/s083/sessions/${sessionId}/criterion/reveal`,
      { contentRevision },
    ),
  openSource: (sessionId: string, contentRevision: string) =>
    command<SourceOpen>(`/api/v1/s083/sessions/${sessionId}/source/open`, {
      contentRevision,
    }),
  getSource: (sessionId: string) =>
    request<{ sourceHtml: string }>(
      `/api/v1/s083/sessions/${sessionId}/source`,
    ),
  getCriterion: (sessionId: string) =>
    request<Criterion>(`/api/v1/s083/sessions/${sessionId}/criterion`),
  scheduleRetrieval: (
    sessionId: string,
    contentRevision: string,
    dueAt: string,
  ) =>
    command<CommandAccepted>(
      `/api/v1/s083/sessions/${sessionId}/retrieval-schedule`,
      { contentRevision, dueAt },
    ),
}
