# Installs the runner as a desktop app. Run it once, and again after each code change.
#   1. Stops the installed app, if it runs (App.Host.exe quit).
#   2. Publishes App/Host to %LOCALAPPDATA%\Runner\app.
#   3. Adds the "Runner" shortcut to the desktop and to the Start menu.
# Then start the runner with the shortcut, as any other app. The app gets its tokens itself (op read), so it needs
# OP_SERVICE_ACCOUNT_TOKEN in the user environment. The mode (real or mock) is a setting of the app.
$ErrorActionPreference = 'Stop'
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
# GitHub Packages needs a token to restore the VSA packages (nuget.config).
if (-not $env:GITHUB_TOKEN) { $env:GITHUB_TOKEN = gh auth token }

$data = Join-Path $env:LOCALAPPDATA 'Runner'
$app = Join-Path $data 'app'
$staging = Join-Path $data 'app.new'
$exe = Join-Path $app 'App.Host.exe'

# Publish to a staging folder first, so a failed build keeps the installed app.
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish (Join-Path $PSScriptRoot 'App\Host') -c Release -o $staging --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed. The installed app did not change.' }

# Stop the installed app. "quit" waits until it has stopped.
$wasRunning = $false
if (Test-Path $exe) {
    & $exe quit | Out-Host
    $wasRunning = $LASTEXITCODE -eq 0
    Get-Process -Name 'App.Host' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue
}

# The files of the old Runner.Host (before the tray app) go to old\. The new app does not use them.
$old = Join-Path $data 'old'
foreach ($name in 'run-runner.ps1', 'runner-host.db', 'runner-host.db-shm', 'runner-host.db-wal', 'runner.err.log', 'runner.secrets.env') {
    $path = Join-Path $data $name
    if (Test-Path $path) {
        New-Item -ItemType Directory -Force $old | Out-Null
        Move-Item $path (Join-Path $old $name) -Force
    }
}
if ((Test-Path (Join-Path $app 'Runner.Host.exe')) -and -not (Test-Path $exe)) {
    New-Item -ItemType Directory -Force $old | Out-Null
    Move-Item $app (Join-Path $old 'app') -Force
}

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Move-Item $staging $app

$shell = New-Object -ComObject WScript.Shell
$places = @([Environment]::GetFolderPath('Desktop'), (Join-Path ([Environment]::GetFolderPath('Programs')) ''))
foreach ($folder in $places) {
    $link = $shell.CreateShortcut((Join-Path $folder 'Runner.lnk'))
    $link.TargetPath = $exe
    $link.WorkingDirectory = $app
    $link.IconLocation = "$exe,0"
    $link.Description = 'Runner: the inbox runner in the tray'
    $link.Save()
}
Write-Host "Installed in $app. Start it with the Runner shortcut on the desktop or in the Start menu."
if ($wasRunning) {
    Write-Host 'The app ran before the install. Start it again with the shortcut.'
}
