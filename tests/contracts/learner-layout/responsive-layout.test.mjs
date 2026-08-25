import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const styles = await readFile(
  new URL(
    '../../../apps/learner-web/src/App.css',
    import.meta.url,
  ),
  'utf8',
)
const globalStyles = await readFile(
  new URL(
    '../../../apps/learner-web/src/index.css',
    import.meta.url,
  ),
  'utf8',
)

test('the hero and learning grid share the same main-container edges', () => {
  assert.match(
    styles,
    /\.learning-grid\s*\{[\s\S]*?margin:\s*0 0 6rem;/,
  )
})

test('tablet and mobile layouts put the active learning act before vocabulary', () => {
  assert.match(
    styles,
    /@media \(max-width: 900px\)[\s\S]*?grid-template-areas:\s*"action"\s*"vocabulary";/,
  )
})

test('narrow mobile headers stack instead of competing for one row', () => {
  assert.match(
    styles,
    /@media \(max-width: 600px\)[\s\S]*?\.site-header\s*\{[\s\S]*?flex-direction:\s*column;/,
  )
})

test('the minimum supported viewport does not force horizontal overflow', () => {
  assert.doesNotMatch(globalStyles, /min-width:\s*320px/)
})
