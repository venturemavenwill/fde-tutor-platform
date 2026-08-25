import { readFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const manifest = JSON.parse(
  await readFile(
    join(root, 'infra', 'identity', 'entra-app-roles.json'),
    'utf8',
  ),
)
const expectedRoles = [
  'Learner',
  'Instructor',
  'Reviewer',
  'Author',
  'Administrator',
  'Operator',
]
const roles = manifest.appRoles ?? []

if (roles.length !== expectedRoles.length) {
  throw new Error(`Expected ${expectedRoles.length} Entra app roles; found ${roles.length}.`)
}

const values = roles.map((role) => role.value)
if (JSON.stringify(values) !== JSON.stringify(expectedRoles)) {
  throw new Error(`Entra app roles must remain in the G-02 order: ${expectedRoles.join(', ')}.`)
}

const ids = new Set()
for (const role of roles) {
  if (
    !Array.isArray(role.allowedMemberTypes) ||
    role.allowedMemberTypes.length !== 1 ||
    role.allowedMemberTypes[0] !== 'User'
  ) {
    throw new Error(`${role.value} must be assignable only to users or groups.`)
  }
  if (!role.isEnabled) {
    throw new Error(`${role.value} must remain enabled.`)
  }
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(role.id)) {
    throw new Error(`${role.value} does not have a valid stable UUID.`)
  }
  if (ids.has(role.id)) {
    throw new Error(`Duplicate Entra app role ID: ${role.id}.`)
  }
  ids.add(role.id)
}

console.log(JSON.stringify({ roleCount: roles.length, roles: values }))
