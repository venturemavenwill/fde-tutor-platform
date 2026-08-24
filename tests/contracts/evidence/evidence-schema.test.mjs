import assert from 'node:assert/strict'
import test from 'node:test'
import {
  createValidator,
  validateDocument,
} from '../../../tools/validate-contracts.mjs'

const baseEvaluation = {
  schemaVersion: '1.0.0',
  evaluationId: '11111111-1111-1111-1111-111111111111',
  tenantId: '22222222-2222-2222-2222-222222222222',
  learnerId: '33333333-3333-3333-3333-333333333333',
  sourceEventIds: ['44444444-4444-4444-4444-444444444444'],
  contentNodeId: 'S083',
  contentRevision: 'a'.repeat(64),
  assessmentPosture: 'NON_ASSESSMENT',
  masteryEffect: 'NONE',
  observedCompetencyIds: ['momentum-decay.design-reinforcement'],
  ordinalLevel: 'RECALLED_UNAIDED',
  narrative: 'The learner named a customer-owned trigger without a cue.',
  supportUsed: ['NONE'],
  grounding: {
    result: 'NOT_APPLICABLE',
    passageIds: [],
  },
  provenance: {
    rubricVersion: '1',
    policyVersion: 's083-policy-1',
    evaluatorVersion: 'deterministic-1',
    modelVersion: null,
    retrievalPolicyVersion: '1',
  },
  reviewState: 'REVIEW_REQUIRED',
}

test('narrative ordinal evidence is valid', async () => {
  const ajv = await createValidator()
  assert.doesNotThrow(() =>
    validateDocument(
      ajv,
      'https://fde-tutor.local/schemas/evidence-evaluation.schema.json',
      baseEvaluation,
      'valid evidence',
    ),
  )
})

test('numeric mastery cannot enter an evidence record', async () => {
  const ajv = await createValidator()
  assert.throws(
    () =>
      validateDocument(
        ajv,
        'https://fde-tutor.local/schemas/evidence-evaluation.schema.json',
        { ...baseEvaluation, masteryPercentage: 80 },
        'numeric evidence',
      ),
    /failed schema validation/,
  )
})

test('ordinal evidence without narrative is rejected', async () => {
  const ajv = await createValidator()
  assert.throws(
    () =>
      validateDocument(
        ajv,
        'https://fde-tutor.local/schemas/evidence-evaluation.schema.json',
        { ...baseEvaluation, narrative: '' },
        'unsupported evidence',
      ),
    /failed schema validation/,
  )
})
