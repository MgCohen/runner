# Runs the runner from the source, for development. To use the runner as an app, run install.ps1 once and start it
# with the "Runner" shortcut on the desktop.
# Usage: .\tray.ps1           Real mode. The listener starts as saved in switches.json.
#        .\tray.ps1 -Paused   Real mode. The listener starts off.
#        .\tray.ps1 -Mock     Mock mode: fake inbox, Notion and Claude. No network, no claude process.
#        -SelfTest            Runs the self-test and exits. It starts paused. Mock: 29 checks and screenshots in
#                             %LOCALAPPDATA%\Runner\selftest-mock\selftest. Real: screenshots in %LOCALAPPDATA%\Runner\selftest-real.
# The app gets its tokens from 1Password itself (op read, deploy/runner.secrets.env). The log and the output of
# claude -p show in this window. The app also writes the log to logs\runner-YYYY-MM-DD.log in its data folder.
# One app runs at a time: if the installed app runs, this start only shows its window. The self-test runs apart.
param([switch] $Mock, [switch] $Paused, [switch] $SelfTest)
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
# GitHub Packages needs a token to restore the VSA packages (nuget.config).
if (-not $env:GITHUB_TOKEN) { $env:GITHUB_TOKEN = gh auth token }
$project = Join-Path $PSScriptRoot 'App\Host'
dotnet build $project -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The build of $project failed. Run: dotnet build $project" }
# An explicit mode: a start from this script does not use or change the saved mode of the app.
$appArgs = @(if ($Mock) { '--mock' } else { '--real' })
if ($Paused) { $appArgs += '--paused' }
if ($SelfTest) { $appArgs += '--selftest' }
Write-Host "Runner from the source. Click the tray icon to open it. Stop it with Exit in the tray menu."
dotnet run --no-build --project $project -- @appArgs
exit $LASTEXITCODE
