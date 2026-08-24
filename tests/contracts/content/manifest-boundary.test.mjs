import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const manifest = JSON.parse(
  await readFile(
    new URL('../../../content-package/manifest.json', import.meta.url),
    'utf8',
  ),
)

test('the manifest hashes exactly the canonical S083 node files', () => {
  assert.deepEqual(Object.keys(manifest.nodes[0].hashes).sort(), [
    'assessments.json',
    'citations.json',
    'competencies.json',
    'content.html',
    'node.json',
    'pedagogy.json',
  ])
  assert.equal(
    Object.keys(manifest.nodes[0].hashes).some(
      (name) => name.includes('..') || name.includes('/') || name.includes('\\'),
    ),
    false,
  )
})

test('all hashed package inputs use canonical LF bytes', async () => {
  const inputs = [
    '../../../content-package/graph.json',
    ...Object.keys(manifest.nodes[0].hashes).map(
      (name) => `../../../content-package/nodes/S083/${name}`,
    ),
  ]
  for (const input of inputs) {
    const bytes = await readFile(new URL(input, import.meta.url))
    assert.equal(
      bytes.includes(Buffer.from('\r\n')),
      false,
      `${input} contains CRLF bytes`,
    )
  }
})
