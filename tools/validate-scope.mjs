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
  'README.md',
  'package.json',
  'apps/platform-api/Program.cs',
  'apps/learner-web/src/App.tsx',
  'content-package/manifest.json',
  'infra/db/migrations/0001_learner_events.sql',
  'packages/platform-domain/Policy/S083Policy.cs',
  'services/projection-worker/Worker.cs',
  'tests/FdeTutor.Api.Tests/S083ApiTests.cs',
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

console.log(
  JSON.stringify({
    implementationFileCount: implementationFiles.length,
    forbiddenDependencyCount: 0,
    forbiddenRouteCount: 0,
    trackedFileCount: tracked.length,
    requiredPublicPathCount: requiredPublicPaths.length,
    privatePathCount: requiredPrivatePaths.length,
  }),
)
