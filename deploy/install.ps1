# Installs the runner for the current Windows user. It needs no admin rights.
# 1. Publishes Runner.Host to %LOCALAPPDATA%\Runner\app.
# 2. Copies run-runner.ps1, which keeps the runner running.
# 3. Adds the shortcut "Runner Host" to the Startup folder, so that the runner starts at log-on.
# 4. Starts the runner.
# Set OP_SERVICE_ACCOUNT_TOKEN (1Password) or INBOX_TOKEN as a user environment variable first.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$base = Join-Path $env:LOCALAPPDATA 'Runner'

if (-not ([Environment]::GetEnvironmentVariable('OP_SERVICE_ACCOUNT_TOKEN', 'User') -or
         [Environment]::GetEnvironmentVariable('INBOX_TOKEN', 'User'))) {
    throw 'Set the user environment variable OP_SERVICE_ACCOUNT_TOKEN (1Password) or INBOX_TOKEN first.'
}
if (-not [Environment]::GetEnvironmentVariable('Inbox__StorePath', 'User')) {
    [Environment]::SetEnvironmentVariable('Inbox__StorePath', (Join-Path $base 'runner-host.db'), 'User')
}

# Stop the running copy, so that publish can replace its files.
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -match 'run-runner\.ps1' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Get-Process Runner.Host -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet publish (Join-Path $repo 'src\Runner.Host') -c Release -o (Join-Path $base 'app')
Copy-Item (Join-Path $PSScriptRoot 'run-runner.ps1'), (Join-Path $PSScriptRoot 'runner.secrets.env') $base -Force

$script = Join-Path $base 'run-runner.ps1'
$arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Startup')) 'Runner Host.lnk'))
$link.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$link.Arguments = $arguments
$link.WorkingDirectory = $base
$link.WindowStyle = 7
$link.Description = 'Local runner for the inbox Worker (MgCohen/runner)'
$link.Save()

Start-Process powershell.exe -ArgumentList $arguments -WorkingDirectory $base -WindowStyle Hidden
Write-Host "Runner installed in $base. Log: $base\runner.log"
