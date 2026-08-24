import { createHash } from 'node:crypto'
import { readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const packageRoot = join(root, 'content-package')
const manifestPath = join(packageRoot, 'manifest.json')
const nodeFiles = [
  'node.json',
  'content.html',
  'pedagogy.json',
  'competencies.json',
  'assessments.json',
  'citations.json',
]

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'))
const hashes = {}
for (const file of nodeFiles) {
  const path = join(packageRoot, 'nodes', 'S083', file)
  const bytes = await readFile(path)
  hashes[file] = createHash('sha256').update(bytes).digest('hex')
}

manifest.nodes[0].hashes = hashes
manifest.graph_hash = createHash('sha256')
  .update(await readFile(join(packageRoot, 'graph.json')))
  .digest('hex')
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
manifest.content_revision = createHash('sha256')
  .update(revisionInput, 'utf8')
  .digest('hex')
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8')
console.log(
  `Updated ${Object.keys(hashes).length} S083 content hashes; package revision ${manifest.content_revision}.`,
)
