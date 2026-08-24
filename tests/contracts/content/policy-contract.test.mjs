import test from 'node:test'
import {
  createValidator,
  validateDocument,
} from '../../../tools/validate-contracts.mjs'
import { readFile } from 'node:fs/promises'

test('a representative emitted policy satisfies the policy schema', async () => {
  const policy = JSON.parse(
    await readFile(
      new URL(
        '../../../tests/fixtures/contracts/s083-policy-decision.json',
        import.meta.url,
      ),
      'utf8',
    ),
  )
  const ajv = await createValidator()
  validateDocument(
    ajv,
    'https://fde-tutor.local/schemas/pedagogical-policy.schema.json',
    policy,
    'S083 policy fixture',
  )
})
