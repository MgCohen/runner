# Runs the runner: the tray app App.Host. The runner runs in the tray app process. Nothing runs in the background.
# Usage: .\tray.ps1           Real mode. The listener starts as saved in switches.json.
#        .\tray.ps1 -Paused   Real mode. The listener starts off.
#        .\tray.ps1 -Mock     Mock mode: fake inbox, Notion and Claude. No network, no claude process.
#        -SelfTest            Runs the self-test and exits. It starts paused. Mock: 29 checks and screenshots in
#                             %LOCALAPPDATA%\Runner\mock\selftest. Real: screenshots in %LOCALAPPDATA%\Runner\selftest-real.
# Stop it with Exit in the tray menu. Keep this window open: the logs and claude -p show here.
# The tokens come from 1Password (deploy/runner.secrets.env) through op run.
param([switch] $Mock, [switch] $Paused, [switch] $SelfTest)
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
    $env:OP_SERVICE_ACCOUNT_TOKEN = [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT_TOKEN', 'User')
}
# The working folder of Claude. It must be a folder that Claude Code trusts.
$env:Modules__Claude__WorkingDirectory = [Environment]::GetEnvironmentVariable('Claude__WorkingDirectory', 'User')
if (-not $env:Modules__Claude__WorkingDirectory) { $env:Modules__Claude__WorkingDirectory = Split-Path -Parent $PSScriptRoot }
Write-Host "Claude folder: $env:Modules__Claude__WorkingDirectory"
Write-Host "Runner in the tray app. Click the tray icon to open it. Stop with Exit in the tray menu."

$project = Join-Path $PSScriptRoot 'App\Host'
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
exit $LASTEXITCODE
