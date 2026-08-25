import { execFileSync } from 'node:child_process'
import { readFile, readdir } from 'node:fs/promises'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const ignoredDirectories = new Set([
  '.git',
  'bin',
  'dist',
  'node_modules',
  'obj',
])

async function collect(directory) {
  const files = []
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (ignoredDirectories.has(entry.name)) continue
    const path = join(directory, entry.name)
    if (entry.isDirectory()) files.push(...(await collect(path)))
    else files.push(path)
  }
  return files
}

const implementationRoots = ['apps', 'packages', 'services']
const implementationFiles = (
  await Promise.all(implementationRoots.map((path) => collect(join(root, path))))
).flat()
const forbiddenDependency = /(?:openai|agent-framework|azure-ai-|langchain|semantic-kernel)/i
const dependencyViolations = []
for (const file of implementationFiles.filter((path) =>
  /(?:package\.json|\.csproj|requirements.*\.txt|pyproject\.toml)$/i.test(path),
)) {
  const content = await readFile(file, 'utf8')
  if (forbiddenDependency.test(content)) {
    dependencyViolations.push(relative(root, file))
  }
}
if (dependencyViolations.length > 0) {
  throw new Error(
    `Phase 1 contains unauthorized model/agent dependencies: ${dependencyViolations.join(', ')}`,
  )
}

const apiFiles = implementationFiles.filter(
  (path) =>
    path.includes(`${join('apps', 'platform-api')}`) &&
    /\.(?:cs|yaml|json)$/.test(path),
)
const routeViolations = []
for (const file of apiFiles) {
  const content = await readFile(file, 'utf8')
  if (/Map(?:Get|Post|Put|Delete)\(\s*["'][^"']*(?:tutor|voice|lms|certificate)/i.test(content)) {
    routeViolations.push(relative(root, file))
  }
}
if (routeViolations.length > 0) {
  throw new Error(`Phase 1 exposes unauthorized routes: ${routeViolations.join(', ')}`)
}

const tracked = execFileSync('git', ['ls-files'], {
  cwd: root,
  encoding: 'utf8',
})
  .trim()
  .split(/\r?\n/)
  .filter(Boolean)

const requiredPrivatePaths = [
  'AGENTS.md',
  '.github/copilot-instructions.md',
  '.github/instructions/fde-tutor-knowledge.instructions.md',
  'docs/design-brief.md',
  'backlog/phase0-1.json',
  'infra/azure/environment.local.json',
]
for (const path of requiredPrivatePaths) {
  try {
    execFileSync('git', ['check-ignore', '--quiet', '--', path], { cwd: root })
  } catch {
    throw new Error(`Private local path is not ignored: ${path}`)
  }
}

const requiredPublicPaths = [
  '.gitignore',
  '.dockerignore',
  'Dockerfile',
  'README.md',
  'start-azure-fde-tutor.cmd',
  'stop-azure-fde-tutor.cmd',
  'launch-fde-tutor.cmd',
  'package.json',
  'apps/platform-api/Access/AccessEndpoints.cs',
  'apps/platform-api/Program.cs',
  'apps/learner-web/src/components/AccessConsole.tsx',
  'apps/learner-web/src/App.tsx',
  'content-package/manifest.json',
  'infra/db/migrations/0002_platform_users.sql',
  'infra/azure/main.bicep',
  'infra/azure/resources.bicep',
  'infra/azure/deploy.ps1',
  'infra/azure/lifecycle.ps1',
  'infra/azure/verify-recovery.ps1',
  'infra/azure/environment.ps1',
  'infra/azure/environment.example.json',
  'infra/identity/entra-app-roles.json',
  'infra/db/migrations/0001_learner_events.sql',
  'packages/platform-domain/Authorization/Phase1AuthorizationMatrix.cs',
  'packages/platform-domain/Policy/S083Policy.cs',
  'services/projection-worker/Worker.cs',
  'tests/FdeTutor.Api.Tests/S083ApiTests.cs',
  'tools/launch-fde-tutor.ps1',
  'tools/FdeTutor.PersistenceEvidence/Program.cs',
  'tools/validate-identity.mjs',
  'tools/validate-contracts.mjs',
]
for (const path of requiredPublicPaths) {
  try {
    execFileSync('git', ['check-ignore', '--quiet', '--', path], { cwd: root })
    throw new Error(`Public implementation path is incorrectly ignored: ${path}`)
  } catch (error) {
    if (error instanceof Error &&
        error.message.startsWith('Public implementation path is incorrectly ignored')) {
      throw error
    }
  }
}

// No committed infrastructure file may name a real environment. Subscription,
// tenant, and application GUIDs and live host names belong in the git-ignored
// environment.local.json, never in tracked scripts or templates.
const environmentIdentifier =
  /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[a-z0-9-]+\.azurecontainerapps\.io|[a-z0-9-]+\.postgres\.database\.azure\.com/gi
const allowedIdentifiers = new Set([
  // Documentation placeholder.
  '00000000-0000-0000-0000-000000000000',
  // Microsoft's well-known public Azure CLI client ID. It is the same in every
  // tenant, is published by Microsoft, and identifies no private environment.
  '04b07795-8ddb-461a-bbee-02f9e1bf7b46',
])
// A host preceded by one of these is PowerShell interpolation such as
// "Host=$ServerName.postgres.database.azure.com", not a literal endpoint.
const interpolationPrefixes = new Set(['$', ')', '}'])
const environmentLeaks = []
for (const path of tracked.filter(
  (candidate) =>
    candidate.startsWith('infra/azure/') &&
    candidate !== 'infra/azure/environment.example.json',
)) {
  const content = await readFile(join(root, path), 'utf8')
  for (const match of content.matchAll(environmentIdentifier)) {
    const value = match[0]
    if (allowedIdentifiers.has(value.toLowerCase())) continue
    if (
      match.index > 0 &&
      interpolationPrefixes.has(content[match.index - 1])
    ) {
      continue
    }
    environmentLeaks.push(`${path}: ${value}`)
  }
}
if (environmentLeaks.length > 0) {
  throw new Error(
    `Committed infrastructure names a real environment: ${environmentLeaks.join(', ')}`,
  )
}

console.log(
  JSON.stringify({
    implementationFileCount: implementationFiles.length,
    forbiddenDependencyCount: 0,
    forbiddenRouteCount: 0,
    trackedFileCount: tracked.length,
    requiredPublicPathCount: requiredPublicPaths.length,
    privatePathCount: requiredPrivatePaths.length,
    environmentLeakCount: 0,
  }),
)
