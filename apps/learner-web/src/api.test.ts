import { expect, test, vi } from 'vitest'
import { api } from './api'

test('reuses a pending idempotency key after a lost response', async () => {
  const observedKeys: string[] = []
  const observedRoles: string[] = []
  let attempt = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      observedKeys.push(new Headers(init?.headers).get('Idempotency-Key') ?? '')
      observedRoles.push(new Headers(init?.headers).get('X-Fde-Roles') ?? '')
      attempt += 1
      if (attempt === 1) {
        throw new TypeError('Simulated connection loss after submission')
      }
      return new Response('{}', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }),
  )
  localStorage.setItem(
    'fde-tutor:idempotency:another-account:/api/v1/s083/sessions',
    '{"fingerprint":"stale","idempotencyKey":"stale-key"}',
  )

  await expect(api.startSession('a'.repeat(64))).rejects.toThrow(
    'For local review, start the app with launch-fde-tutor.cmd and keep its window open.',
  )
  await api.startSession('a'.repeat(64))

  expect(observedKeys[0]).not.toBe('')
  expect(observedKeys[1]).toBe(observedKeys[0])
  expect(observedRoles).toEqual([
    'Learner,Administrator',
    'Learner,Administrator',
  ])
  expect(
    Array.from({ length: localStorage.length }, (_, index) =>
      localStorage.key(index),
    ).filter((key) => key?.startsWith('fde-tutor:idempotency:')),
  ).toEqual([])
})
