import assert from 'node:assert/strict'
import test from 'node:test'
import {
  createValidator,
  validateDocument,
} from '../../../tools/validate-contracts.mjs'

const base = {
  eventId: '11111111-1111-1111-1111-111111111111',
  eventType: 'RetrievalScheduled',
  eventVersion: 1,
  occurredAt: '2026-08-24T10:00:00Z',
  recordedAt: '2026-08-24T10:00:01Z',
  tenantId: '22222222-2222-2222-2222-222222222222',
  learnerId: '33333333-3333-3333-3333-333333333333',
  sessionId: '44444444-4444-4444-4444-444444444444',
  contentNodeId: 'S083',
  contentRevision: 'a'.repeat(64),
  correlationId: '55555555-5555-5555-5555-555555555555',
  causationId: '66666666-6666-6666-6666-666666666666',
  idempotencyKey: 'idempotency-key',
  actor: {
    type: 'learner',
    id: 'subject',
  },
}

test('retrieval scheduling requires a due date and mode', async () => {
  const ajv = await createValidator()
  assert.doesNotThrow(() =>
    validateDocument(
      ajv,
      'https://fde-tutor.local/schemas/learner-event.schema.json',
      {
        ...base,
        payload: {
          dueAt: '2026-08-26T10:00:00Z',
          mode: 'CHANGED_CONTEXT_SAME_NODE',
        },
      },
      'valid retrieval schedule',
    ),
  )
  assert.throws(
    () =>
      validateDocument(
        ajv,
        'https://fde-tutor.local/schemas/learner-event.schema.json',
        { ...base, payload: {} },
        'invalid retrieval schedule',
      ),
    /failed schema validation/,
  )
})

test('authentic transfer requires classification and pilot restriction', async () => {
  const ajv = await createValidator()
  const event = {
    ...base,
    eventType: 'ArtifactSubmitted',
    payload: {
      response: 'A synthetic drift register with no customer-confidential data.',
      classification: 'AUTHENTIC_WORK',
      pilotRestriction: 'SYNTHETIC_REDACTED_OR_EXPLICITLY_APPROVED',
    },
  }
  assert.doesNotThrow(() =>
    validateDocument(
      ajv,
      'https://fde-tutor.local/schemas/learner-event.schema.json',
      event,
      'valid authentic transfer',
    ),
  )
  assert.throws(
    () =>
      validateDocument(
        ajv,
        'https://fde-tutor.local/schemas/learner-event.schema.json',
        {
          ...event,
          payload: {
            response: 'Unclassified work.',
          },
        },
        'unclassified authentic transfer',
      ),
    /failed schema validation/,
  )
})
