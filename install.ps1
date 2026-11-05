# Installs the runner as a desktop app. Run it once, and again after each code change.
#   1. Publishes App/Host to %USERPROFILE%\Apps\Runner.
#   2. Stops the app, if it runs (App.Host.exe quit), and puts the new files in place.
#   3. Adds the "Runner" shortcut to the desktop and to the Start menu.
# Then start the runner with the shortcut, as any other app. The app gets its tokens itself (op read), so it needs
# OP_SERVICE_ACCOUNT_TOKEN in the user environment. The mode (real or mock) is a setting of the app.
# The data stays in %LOCALAPPDATA%\Runner.
#
# The app is not in AppData: a packaged app (for example the Claude desktop app) that runs this script writes AppData
# to its own private folder, and Explorer does not see files there. Run from such an app, the Start menu shortcut
# (in AppData\Roaming) also goes to that private folder. The desktop shortcut works from all places.
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
# GitHub Packages needs a token to restore the VSA packages (nuget.config).
if (-not $env:GITHUB_TOKEN) { $env:GITHUB_TOKEN = gh auth token }

$data = Join-Path $env:LOCALAPPDATA 'Runner'
$apps = Join-Path $env:USERPROFILE 'Apps'
$app = Join-Path $apps 'Runner'
$staging = Join-Path $apps 'Runner.new'
$exe = Join-Path $app 'App.Host.exe'

# Publish to a staging folder first, so a failed build keeps the installed app.
New-Item -ItemType Directory -Force $apps | Out-Null
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish (Join-Path $PSScriptRoot 'App\Host') -c Release -o $staging --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed. The installed app did not change.' }

# Stop the app that runs, from any folder: "quit" goes to the app on a named pipe and waits until it has stopped.
# "quit" writes to stderr when the app does not run. Windows PowerShell 5.1 with 'Stop' makes that an error.
$ErrorActionPreference = 'Continue'
& (Join-Path $staging 'App.Host.exe') quit 2>$null | Out-Host
$wasRunning = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
Get-Process -Name 'App.Host' -ErrorAction SilentlyContinue | Where-Object { $_.Path -notlike "$staging*" } | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue

# The files of the old Runner.Host (before the tray app) go to old\. The new app does not use them.
$old = Join-Path $data 'old'
foreach ($name in 'run-runner.ps1', 'runner-host.db', 'runner-host.db-shm', 'runner-host.db-wal', 'runner.err.log', 'runner.secrets.env') {
    $path = Join-Path $data $name
    if (Test-Path $path) {
        New-Item -ItemType Directory -Force $old | Out-Null
        Move-Item $path (Join-Path $old $name) -Force
    }
}
# The app was in %LOCALAPPDATA%\Runner\app before. That folder goes.
$oldApp = Join-Path $data 'app'
if (Test-Path $oldApp) { Remove-Item $oldApp -Recurse -Force -ErrorAction SilentlyContinue }

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Move-Item $staging $app

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) {
    $path = Join-Path $folder 'Runner.lnk'
    if (Test-Path $path) { Remove-Item $path -Force }
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $app
    $link.IconLocation = "$exe,0"
    $link.Description = 'Runner: the inbox runner in the tray'
    $link.Save()
}
Write-Host "Installed in $app. Start it with the Runner shortcut on the desktop or in the Start menu."
Write-Host 'The first start after an install can take some minutes: the virus scanner examines the new files.'
if ($wasRunning) {
    Write-Host 'The app ran before the install. Start it again with the shortcut.'
}
exit 0
