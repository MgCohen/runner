# Keeps the runner running. A shortcut in the Startup folder starts this file hidden at log-on.
# If the runner stops, this script starts it again after 30 seconds.
# Secrets: if OP_SERVICE_ACCOUNT_TOKEN is set, the runner starts through `op run` with
# runner.secrets.env (1Password vault "agent"). If not, it uses the user environment variables.
# The output goes to runner.log. The log of the previous run is kept as runner.old.log.
$base = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $base 'runner.log'
$err = Join-Path $base 'runner.err.log'
$exe = Join-Path $base 'app\Runner.Host.exe'
$secrets = Join-Path $base 'runner.secrets.env'
while ($true) {
    # Read the settings from the user registry, so that changes apply without a new log-on.
    foreach ($name in 'OP_SERVICE_ACCOUNT_TOKEN', 'INBOX_TOKEN', 'NOTION_API_TOKEN', 'Inbox__StorePath',
                      'Claude__WorkingDirectory', 'Handlers__NotionPageUpdated__Enabled') {
        $value = [Environment]::GetEnvironmentVariable($name, 'User')
        if ($value) { [Environment]::SetEnvironmentVariable($name, $value, 'Process') }
    }
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $op = Get-Command op -ErrorAction SilentlyContinue

    if (Test-Path $log) { Move-Item $log (Join-Path $base 'runner.old.log') -Force }
    if ($env:OP_SERVICE_ACCOUNT_TOKEN -and $op -and (Test-Path $secrets)) {
        $file = $op.Source
        $arguments = @('run', '--env-file', "`"$secrets`"", '--', "`"$exe`"")
    } else {
        $file = $exe
        $arguments = @()
    }
    $start = @{ FilePath = $file; WorkingDirectory = (Join-Path $base 'app'); WindowStyle = 'Hidden'; PassThru = $true
                RedirectStandardOutput = $log; RedirectStandardError = $err }
    if ($arguments.Count) { $start.ArgumentList = $arguments }
    $p = Start-Process @start
    $p.WaitForExit()
    Start-Sleep -Seconds 30
}
