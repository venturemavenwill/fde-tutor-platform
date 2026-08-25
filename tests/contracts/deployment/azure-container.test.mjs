import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const dockerfile = await readFile(
  new URL('../../../Dockerfile', import.meta.url),
  'utf8',
)
const infrastructure = await readFile(
  new URL('../../../infra/azure/resources.bicep', import.meta.url),
  'utf8',
)
const deployment = await readFile(
  new URL('../../../infra/azure/deploy.ps1', import.meta.url),
  'utf8',
)
const recovery = await readFile(
  new URL('../../../infra/azure/verify-recovery.ps1', import.meta.url),
  'utf8',
)
const lifecycle = await readFile(
  new URL('../../../infra/azure/lifecycle.ps1', import.meta.url),
  'utf8',
)
const startCommand = await readFile(
  new URL('../../../start-azure-fde-tutor.cmd', import.meta.url),
  'utf8',
)
const stopCommand = await readFile(
  new URL('../../../stop-azure-fde-tutor.cmd', import.meta.url),
  'utf8',
)
const program = await readFile(
  new URL('../../../apps/platform-api/Program.cs', import.meta.url),
  'utf8',
)
const environmentExample = await readFile(
  new URL('../../../infra/azure/environment.example.json', import.meta.url),
  'utf8',
)
const gitignore = await readFile(
  new URL('../../../.gitignore', import.meta.url),
  'utf8',
)
const parameters = await readFile(
  new URL('../../../infra/azure/main.parameters.json', import.meta.url),
  'utf8',
)

test('deployment scripts read the environment from an ignored local file', () => {
  for (const script of [deployment, lifecycle, recovery]) {
    assert.match(script, /\. \(Join-Path \$PSScriptRoot 'environment\.ps1'\)/)
    assert.match(script, /Get-FdeTutorEnvironment/)
    assert.match(script, /Get-FdeTutorSetting/)
  }

  assert.match(gitignore, /^\/infra\/azure\/environment\.local\.json$/m)

  const example = JSON.parse(environmentExample)
  for (const key of [
    'subscriptionId',
    'tenantId',
    'location',
    'environmentName',
    'resourceGroupName',
    'containerAppName',
    'postgresServerName',
    'restoredPostgresServerName',
    'keyVaultName',
    'applicationUrl',
    'apiScope',
    'evidenceSessionId',
  ]) {
    assert.ok(key in example, `environment.example.json is missing ${key}`)
  }
})

test('no committed infrastructure file names a real environment', () => {
  const realIdentifier =
    /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[a-z0-9-]+\.azurecontainerapps\.io|[a-z0-9-]+\.postgres\.database\.azure\.com/gi
  const allowed = new Set([
    '00000000-0000-0000-0000-000000000000',
    // Microsoft's published Azure CLI public client ID, identical in every
    // tenant and not an environment identifier.
    '04b07795-8ddb-461a-bbee-02f9e1bf7b46',
  ])
  const interpolationPrefixes = new Set(['$', ')', '}'])

  for (const [name, content] of [
    ['deploy.ps1', deployment],
    ['lifecycle.ps1', lifecycle],
    ['verify-recovery.ps1', recovery],
    ['resources.bicep', infrastructure],
    ['main.parameters.json', parameters],
  ]) {
    for (const match of content.matchAll(realIdentifier)) {
      const value = match[0].toLowerCase()
      if (allowed.has(value)) continue
      if (
        match.index > 0 &&
        interpolationPrefixes.has(content[match.index - 1])
      ) {
        continue
      }
      assert.fail(`${name} leaks a real environment identifier: ${match[0]}`)
    }
  }
})

test('the production container serves the SPA and API on the ACA target port', () => {
  assert.match(dockerfile, /FROM node:24-bookworm-slim AS web-build/)
  assert.match(dockerfile, /FROM mcr\.microsoft\.com\/dotnet\/aspnet:10\.0-noble/)
  assert.match(dockerfile, /libgssapi-krb5-2/)
  assert.match(dockerfile, /COPY --from=web-build .*\/dist \.\/wwwroot/)
  assert.match(dockerfile, /ASPNETCORE_URLS=http:\/\/\+:8080/)
  assert.match(dockerfile, /EXPOSE 8080/)
  assert.match(dockerfile, /USER \$APP_UID/)
  assert.match(program, /UseStaticFiles\(\)/)
  assert.match(program, /MapFallbackToFile\("index\.html"\)/)
})

test('the isolated Azure environment uses the selected minimal evidence resources', () => {
  assert.match(infrastructure, /Standard_B1ms/)
  assert.match(infrastructure, /backupRetentionDays:\s*7/)
  assert.match(infrastructure, /version:\s*'17'/)
  assert.match(infrastructure, /acrSku:\s*'Basic'/)
  assert.match(infrastructure, /user-assigned-identity:0\.6\.0/)
  assert.match(infrastructure, /Key Vault Secrets User/)
  assert.match(
    infrastructure,
    /mcr\.microsoft\.com\/azuredocs\/containerapps-helloworld:latest/,
  )
})

test('deployment runs what-if, remote build, Entra setup, and evidence-only guards', () => {
  const whatIf = deployment.indexOf('deployment sub what-if')
  const provision = deployment.indexOf(
    "Write-Step 'Provisioning the isolated Azure resources.'",
  )
  assert.ok(whatIf >= 0 && provision > whatIf)
  assert.match(deployment, /az acr build/)
  assert.match(deployment, /FDE Tutor Technical API/)
  assert.match(deployment, /FDE Tutor Technical SPA/)
  assert.match(
    deployment,
    /preAuthorizedApplications'\] = @\([\s\S]*?@\{[\s\S]*?appId = \$spaApplication\.appId[\s\S]*?@\{[\s\S]*?appId = \$azureCliAppId/,
  )
  assert.match(deployment, /Deployment__EvidenceOnly=true/)
  assert.match(deployment, /ASPNETCORE_ENVIRONMENT=TechnicalEvidence/)
  assert.match(
    program,
    /TechnicalEvidence requires Entra, PostgreSQL, and Deployment:EvidenceOnly=true/,
  )
})

test('recovery verification proves restored state and always returns to source', () => {
  assert.match(recovery, /Set-DatabaseSecret -ServerName \$RestoredServerName/)
  assert.match(recovery, /restoredState\.policy\.state -ne 'Complete'/)
  assert.match(recovery, /finally\s*\{[\s\S]*Set-DatabaseSecret -ServerName \$SourceServerName/)
  assert.match(recovery, /sourceState\.policy\.state -ne 'Complete'/)
  assert.match(recovery, /flexible-server',[\s\S]*'delete'/)
})

test('paired lifecycle starts PostgreSQL first and stops it after deallocation', () => {
  const startDatabase = lifecycle.search(/'flexible-server',\r?\n\s*'start'/)
  const enableIngress = lifecycle.search(/'ingress',\r?\n\s*'enable'/)
  const disableIngress = lifecycle.search(/'ingress',\r?\n\s*'disable'/)
  const stopDatabase = lifecycle.search(/'flexible-server',\r?\n\s*'stop'/)
  assert.ok(startDatabase >= 0 && enableIngress > startDatabase)
  assert.ok(disableIngress >= 0 && stopDatabase > disableIngress)
  assert.match(lifecycle, /'revision',\r?\n\s*'deactivate'/)
  assert.match(lifecycle, /'revision',\r?\n\s*'activate'/)
  assert.match(lifecycle, /if \(\(Get-ReplicaCount\) -ne 0\)[\s\S]*PostgreSQL was left running/)
  assert.match(infrastructure, /triggerType:\s*'Schedule'/)
  assert.match(infrastructure, /cronExpression:\s*'0 3 \* \* \*'/)
  assert.match(infrastructure, /fdeLifecycle/)
  assert.match(startCommand, /exit \/b %lifecycleExitCode%/)
  assert.match(stopCommand, /exit \/b %lifecycleExitCode%/)
})
