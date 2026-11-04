# Runs the runner in this window. Stop it with Ctrl+C. Nothing runs in the background.
# Usage: .\host.ps1            Receives inbox events and logs them. No handler starts Claude.
#        .\host.ps1 -Claude    Also turns on the handlers that start Claude sessions.
# The tokens come from 1Password (deploy/runner.secrets.env) through op run.
param([switch] $Claude)
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
    $env:OP_SERVICE_ACCOUNT_TOKEN = [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT_TOKEN', 'User')
}
$on = if ($Claude) { 'true' } else { 'false' }
$env:Handlers__Tasks__Enabled = $on
$env:Handlers__NotionPageUpdated__Enabled = $on
$env:Inbox__StorePath = Join-Path $env:LOCALAPPDATA 'Runner\runner-host.db'
Write-Host "Runner in this window. Claude handlers: $on. Stop with Ctrl+C."

$project = Join-Path $PSScriptRoot 'src\Runner.Host'
dotnet build $project -v q --nologo | Out-Null
op run --env-file (Join-Path $PSScriptRoot 'deploy\runner.secrets.env') -- dotnet run --no-build --project $project
