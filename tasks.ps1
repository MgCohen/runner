# Runs the Notion Todo tasks in this window. Nothing runs in the background.
# Usage: .\tasks.ps1 [--list] [--yes]
# The Notion token comes from 1Password (deploy/runner.secrets.env) through op run.
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
if (-not $env:OP_SERVICE_ACCOUNT_TOKEN) {
    $env:OP_SERVICE_ACCOUNT_TOKEN = [Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT_TOKEN', 'User')
}
$project = Join-Path $PSScriptRoot 'src\Runner.Cli'
dotnet build $project -v q --nologo | Out-Null
op run --env-file (Join-Path $PSScriptRoot 'deploy\runner.secrets.env') -- dotnet run --no-build --project $project -- tasks @args
