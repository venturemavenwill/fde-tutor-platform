import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import Ajv2020 from 'ajv/dist/2020.js'
import addFormats from 'ajv-formats'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const schemaRoot = join(root, 'packages', 'learning-contract', 'schemas')
const packageRoot = join(root, 'content-package')

const schemaFiles = [
  'content-node.schema.json',
  'learner-event.schema.json',
  'evidence-evaluation.schema.json',
  'pedagogical-policy.schema.json',
  'artifact.schema.json',
]
const expectedNodeFiles = [
  'assessments.json',
  'citations.json',
  'competencies.json',
  'content.html',
  'node.json',
  'pedagogy.json',
]

async function readJson(path) {
  return JSON.parse(await readFile(path, 'utf8'))
}

export async function createValidator() {
  const ajv = new Ajv2020({ allErrors: true, strict: true })
  addFormats(ajv)
  for (const file of schemaFiles) {
    ajv.addSchema(await readJson(join(schemaRoot, file)))
  }
  return ajv
}

export function validateDocument(ajv, schemaId, document, label) {
  const validate = ajv.getSchema(schemaId)
  if (!validate) {
    throw new Error(`Schema '${schemaId}' was not registered.`)
  }
  if (!validate(document)) {
    throw new Error(
      `${label} failed schema validation: ${ajv.errorsText(validate.errors, {
        separator: '\n',
      })}`,
    )
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message)
}

function containsNumericMastery(value) {
  if (Array.isArray(value)) return value.some(containsNumericMastery)
  if (!value || typeof value !== 'object') return false
  return Object.entries(value).some(
    ([key, child]) =>
      (/mastery/i.test(key) && typeof child === 'number') ||
      containsNumericMastery(child),
  )
}

export async function validateAll({ offering = false } = {}) {
  const ajv = await createValidator()
  const manifest = await readJson(join(packageRoot, 'manifest.json'))
  const graphPath = join(packageRoot, 'graph.json')
  const graphBytes = await readFile(graphPath)
  const graph = JSON.parse(graphBytes.toString('utf8'))
  const nodeRoot = join(packageRoot, 'nodes', 'S083')
  const node = await readJson(join(nodeRoot, 'node.json'))
  const pedagogy = await readJson(join(nodeRoot, 'pedagogy.json'))
  const competencies = await readJson(join(nodeRoot, 'competencies.json'))
  const assessments = await readJson(join(nodeRoot, 'assessments.json'))
  const citations = await readJson(join(nodeRoot, 'citations.json'))

  validateDocument(
    ajv,
    'https://fde-tutor.local/schemas/content-node.schema.json',
    node,
    'S083 node',
  )

  assert(
    node.source.commit === manifest.source.commit,
    'S083 node source commit must equal the manifest source commit.',
  )
  assert(
    graph.source_commit === manifest.source.commit,
    'S083 graph source commit must equal the manifest source commit.',
  )
  assert(node.assessment_bearing === false, 'S083 must remain non-assessment-bearing.')
  assert(
    node.criteria.every(
      (criterion) =>
        criterion.assessable === false &&
        criterion.assesses_namespace !== 'platform',
    ),
    'S083 criteria must be non-assessable and may not assess the platform namespace.',
  )
  assert(graph.nodes.length === 1, 'The Phase 1 graph must contain only S083.')
  const graphNode = graph.nodes[0]
  assert(graphNode.id === 'S083', 'The Phase 1 graph node must be S083.')
  assert(
    graphNode.depends_on.length === 0 &&
      graphNode.dependency_closure_size === 1,
    'S083 must retain zero dependency closure.',
  )
  assert(
    JSON.stringify(
      graphNode.retrieves_from
        .map((edge) => `${edge.node_id}:${edge.mode}`)
        .sort(),
    ) ===
      JSON.stringify([
        'S009:UNSCORED_COLD_START',
        'S064:UNSCORED_COLD_START',
        'S078:UNSCORED_COLD_START',
        'S082:UNSCORED_COLD_START',
      ]),
    'S083 must retain the exact four unscored cold-start edges.',
  )
  assert(
    JSON.stringify(
      graphNode.retrieved_by
        .map((edge) => `${edge.node_id}:${edge.mode}`)
        .sort(),
    ) === JSON.stringify(['S084:DEFERRED', 'S085:DEFERRED']),
    'Only deferred S084/S085 carry-forward edges are allowed.',
  )
  assert(
    graphNode.phase1_retrieval?.mode === 'CHANGED_CONTEXT_SAME_NODE' &&
      graphNode.phase1_retrieval?.node_id === 'S083',
    'Phase 1 retrieval must remain changed-context S083.',
  )
  assert(
    pedagogy.unpaid_remedy.required_event === 'UnpaidRemedyRecorded' &&
      pedagogy.unpaid_remedy.locks.includes('four_element_criterion') &&
      pedagogy.unpaid_remedy.locks.includes('paid_proposal_improvement'),
    'S083 must hard-lock the criterion and paid improvement behind UnpaidRemedyRecorded.',
  )
  assert(
    competencies.projection_effect.mastery === 'NONE' &&
      competencies.projection_effect.entrustment === 'NONE' &&
      competencies.projection_effect.carry_forward === 'PROHIBITED',
    'S083 competency projection effects must remain disabled.',
  )
  assert(!containsNumericMastery(competencies), 'Numeric mastery is prohibited.')
  assert(
    assessments.assessment_bearing === false,
    'S083 contrast cases are practice, not assessment.',
  )
  assert(
    citations.platform_grounding.on_missing === 'GROUNDING_REQUIRED',
    'Missing platform grounding must return GROUNDING_REQUIRED.',
  )

  const hashes = manifest.nodes[0].hashes
  assert(
    JSON.stringify(Object.keys(hashes).sort()) ===
      JSON.stringify(expectedNodeFiles),
    'The S083 manifest must contain exactly the six canonical node filenames.',
  )
  for (const file of expectedNodeFiles) {
    const expected = hashes[file]
    const bytes = await readFile(join(nodeRoot, file))
    const actual = createHash('sha256').update(bytes).digest('hex')
    assert(actual === expected, `Content hash mismatch for ${file}.`)
  }
  const graphHash = createHash('sha256').update(graphBytes).digest('hex')
  assert(graphHash === manifest.graph_hash, 'Content graph hash mismatch.')
  const revisionInput = [
    `schema_version=${manifest.schema_version}`,
    `source_commit=${manifest.source.commit}`,
    `upstream_hve_revision=${manifest.source.upstream_hve_revision ?? ''}`,
    `graph_hash=${manifest.graph_hash}`,
    `assessment_bank_version=${manifest.assessment_bank_version}`,
    `namespace_policy_version=${manifest.namespace_policy_version}`,
    `platform_freshness_policy_version=${manifest.platform_freshness_policy_version}`,
    `policy_version=${manifest.policy_version}`,
    `minimum_runtime_version=${manifest.minimum_runtime_version}`,
    ...Object.entries(hashes)
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([file, hash]) => `node:${file}=${hash}`),
  ].join('\n')
  const expectedRevision = createHash('sha256')
    .update(revisionInput, 'utf8')
    .digest('hex')
  assert(
    expectedRevision === manifest.content_revision,
    'Package content revision does not match its immutable digest.',
  )

  if (offering) {
    assert(
      manifest.offering_status === 'OFFERING_APPROVED',
      'The content package is not approved for an offering.',
    )
    assert(
      manifest.source.canonical_owner_confirmed === true,
      'The canonical source owner is not confirmed.',
    )
    assert(
      node.freshness_status === 'CURRENT',
      'Platform-bearing S083 content is not currently verified.',
    )
    assert(
      node.platform_instance_verified_on && node.platform_verify_before,
      'Platform verification dates are required before an offering.',
    )
  }

  return {
    packageId: manifest.package_id,
    contentRevision: manifest.content_revision,
    offeringReady: offering,
    schemaCount: schemaFiles.length,
    hashCount: expectedNodeFiles.length,
  }
}

const isMain = import.meta.url === pathToFileURL(process.argv[1]).href
if (isMain) {
  const result = await validateAll({ offering: process.argv.includes('--offering') })
  console.log(JSON.stringify(result))
}
