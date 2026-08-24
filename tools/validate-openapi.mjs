import { readFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parse } from 'yaml'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const path = join(root, 'apps', 'platform-api', 'openapi', 's083.yaml')
const document = parse(await readFile(path, 'utf8'))

if (document.openapi !== '3.1.0') {
  throw new Error('The S083 OpenAPI contract must use OpenAPI 3.1.0.')
}

const requiredPaths = [
  '/api/v1/s083/content',
  '/api/v1/s083/learning-home',
  '/api/v1/s083/sessions',
  '/api/v1/s083/sessions/{sessionId}',
  '/api/v1/s083/sessions/{sessionId}/expectation',
  '/api/v1/s083/sessions/{sessionId}/cold-start',
  '/api/v1/s083/sessions/{sessionId}/priming',
  '/api/v1/s083/sessions/{sessionId}/unpaid-remedy',
  '/api/v1/s083/sessions/{sessionId}/source',
  '/api/v1/s083/sessions/{sessionId}/source/open',
  '/api/v1/s083/sessions/{sessionId}/criterion',
  '/api/v1/s083/sessions/{sessionId}/criterion/reveal',
  '/api/v1/s083/sessions/{sessionId}/comparison',
  '/api/v1/s083/sessions/{sessionId}/revision',
  '/api/v1/s083/sessions/{sessionId}/authentic-transfer',
  '/api/v1/s083/sessions/{sessionId}/retrieval-schedule',
  '/api/v1/s083/sessions/{sessionId}/retrieval',
]
for (const requiredPath of requiredPaths) {
  if (!document.paths?.[requiredPath]) {
    throw new Error(`The OpenAPI contract is missing ${requiredPath}.`)
  }
}

const forbidden = Object.keys(document.paths).filter((item) =>
  /tutor|voice|lms|certificate|tenant-two/i.test(item),
)
if (forbidden.length > 0) {
  throw new Error(`The Phase 1 API exposes later-phase paths: ${forbidden.join(', ')}`)
}

if (!document.components?.securitySchemes?.entra) {
  throw new Error('The OpenAPI contract must declare Entra OAuth.')
}

const retrievalSchedule =
  document.paths?.['/api/v1/s083/sessions/{sessionId}/retrieval-schedule']?.post
if (
  retrievalSchedule?.requestBody?.content?.['application/json']?.schema?.$ref !==
  '#/components/schemas/RetrievalScheduleRequest'
) {
  throw new Error(
    'The retrieval-schedule operation must require RetrievalScheduleRequest.',
  )
}
if (!document.components?.schemas?.RetrievalScheduleRequest) {
  throw new Error('The OpenAPI contract is missing RetrievalScheduleRequest.')
}

console.log(
  JSON.stringify({
    openapi: document.openapi,
    pathCount: Object.keys(document.paths).length,
    forbiddenPathCount: forbidden.length,
  }),
)
