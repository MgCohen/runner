# Runs the runner in the tray app. The runner runs in the tray app process. Nothing runs in the background.
# Usage: .\tray.ps1           Real mode. The listener starts as saved in switches.json.
#        .\tray.ps1 -Paused   Real mode. The listener starts off.
#        .\tray.ps1 -Mock     Mock mode: fake inbox, Notion and Claude. No network, no claude process.
#        -SelfTest            Runs a fixed test, saves widget images in %LOCALAPPDATA%\Runner\mock\selftest
#                             (mock) or %LOCALAPPDATA%\Runner\selftest-real (real), and exits.
# Stop it with Exit in the tray menu, or with Ctrl+C in this window. Keep this window open.
# The switches (listener, sources, handlers) are in %LOCALAPPDATA%\Runner\switches.json.
# The tokens come from 1Password (deploy/runner.secrets.env) through op run.
param([switch] $Mock, [switch] $Paused, [switch] $SelfTest)
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
    $env:OP_SERVICE_ACCOUNT_TOKEN = [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT_TOKEN', 'User')
}
$env:Handlers__Tasks__Mode = 'Print'
$env:Inbox__StorePath = Join-Path $env:LOCALAPPDATA 'Runner\runner-host.db'
# The working folder of Claude. It must be a folder that Claude Code trusts.
$env:Claude__WorkingDirectory = [Environment]::GetEnvironmentVariable('Claude__WorkingDirectory', 'User')
if (-not $env:Claude__WorkingDirectory) { $env:Claude__WorkingDirectory = Split-Path -Parent $PSScriptRoot }
Write-Host "Claude folder: $env:Claude__WorkingDirectory"
Write-Host "Runner in the tray app. Click the tray icon to open it. Stop with Exit in the tray menu or Ctrl+C."

$project = Join-Path $PSScriptRoot 'src\Runner.Tray'
dotnet build $project -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The build of $project failed. Run: dotnet build $project" }
$appArgs = @()
if ($Mock) { $appArgs += '--mock' }
if ($Paused) { $appArgs += '--paused' }
if ($SelfTest) { $appArgs += '--selftest' }
if ($Mock) {
    dotnet run --no-build --project $project -- @appArgs
} else {
    op run --env-file (Join-Path $PSScriptRoot 'deploy\runner.secrets.env') -- dotnet run --no-build --project $project -- @appArgs
}
