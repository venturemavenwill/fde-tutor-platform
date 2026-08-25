import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const commandLauncher = await readFile(
  new URL('../../../launch-fde-tutor.cmd', import.meta.url),
  'utf8',
)
const powershellLauncher = await readFile(
  new URL('../../../tools/launch-fde-tutor.ps1', import.meta.url),
  'utf8',
)

test('the Windows launcher delegates to the reviewed PowerShell entry point', () => {
  assert.match(commandLauncher, /tools\\launch-fde-tutor\.ps1/)
  assert.match(commandLauncher, /-ExecutionPolicy Bypass/)
  assert.match(commandLauncher, /%\\*/)
  assert.match(commandLauncher, /if not "%launcherExitCode%"=="0"/)
})

test('the launcher uses the bounded Phase 1 development runtime', () => {
  assert.match(powershellLauncher, /\$apiUrl = 'http:\/\/localhost:5080'/)
  assert.match(powershellLauncher, /\$apiReadyUrl = "\$apiUrl\/health\/ready"/)
  assert.match(powershellLauncher, /http:\/\/127\.0\.0\.1:5173/)
  assert.match(powershellLauncher, /--launch-profile/)
  assert.match(powershellLauncher, /--strictPort/)
  assert.match(powershellLauncher, /DEVELOPMENT-ONLY/)
  assert.match(powershellLauncher, /in-memory data/)
})

test('the launcher refuses port conflicts and cleans up exact process IDs', () => {
  assert.match(powershellLauncher, /Port \$port is already in use/)
  assert.match(powershellLauncher, /Get-ListenerProcessId -Port 5080/)
  assert.match(powershellLauncher, /Get-ListenerProcessId -Port 5173/)
  assert.match(powershellLauncher, /Stop-Process -Id \$TargetProcessId/)
  assert.match(powershellLauncher, /Launcher cleanup did not release port \$port/)
  assert.doesNotMatch(powershellLauncher, /Stop-Process -Name/)
  assert.doesNotMatch(powershellLauncher, /taskkill/)
})
