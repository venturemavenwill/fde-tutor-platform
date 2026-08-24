import assert from 'node:assert/strict'
import test from 'node:test'
import {
  createValidator,
  validateAll,
  validateDocument,
} from '../../../tools/validate-contracts.mjs'

test('the development S083 package satisfies all executable invariants', async () => {
  const result = await validateAll()
  assert.equal(result.packageId, 'fde-s083-development')
  assert.equal(result.hashCount, 6)
  assert.equal(result.offeringReady, false)
})

test('the development package fails closed as a cohort offering', async () => {
  await assert.rejects(
    () => validateAll({ offering: true }),
    /not approved for an offering/,
  )
})

test('an assessable platform criterion is rejected', async () => {
  const ajv = await createValidator()
  const invalid = {
    schema_version: '1.0.0',
    id: 'S083',
    title: 'Invalid',
    namespace: 'durable',
    assessment_bearing: false,
    platform_bearing: true,
    freshness_status: 'UNVERIFIED',
    platform_instance_verified_on: null,
    platform_verify_before: null,
    source: {
      repository: 'https://example.test/playbook.git',
      branch: 'main',
      commit: 'a'.repeat(40),
      path: 'S083.md',
    },
    criteria: [
      {
        criterion_id: 'bad.platform',
        statement: 'A current quota value.',
        assessable: true,
        assesses_namespace: 'platform',
      },
    ],
  }

  assert.throws(
    () =>
      validateDocument(
        ajv,
        'https://fde-tutor.local/schemas/content-node.schema.json',
        invalid,
        'invalid node',
      ),
    /failed schema validation/,
  )
})
