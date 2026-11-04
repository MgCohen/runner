# Keeps the runner running. A shortcut in the Startup folder starts this file hidden at log-on.
# If the runner stops, this script starts it again after 30 seconds.
# The output goes to runner.log. The log of the previous run is kept as runner.old.log.
$base = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $base 'runner.log'
$err = Join-Path $base 'runner.err.log'
while ($true) {
    # Read the runner settings from the user registry, so that changes apply without a new log-on.
    foreach ($name in 'INBOX_TOKEN', 'Inbox__StorePath', 'Claude__WorkingDirectory', 'NOTION_API_TOKEN') {
        $value = [Environment]::GetEnvironmentVariable($name, 'User')
        if ($value) { [Environment]::SetEnvironmentVariable($name, $value, 'Process') }
    }
    if (Test-Path $log) { Move-Item $log (Join-Path $base 'runner.old.log') -Force }
    $p = Start-Process (Join-Path $base 'app\Runner.Host.exe') -WorkingDirectory (Join-Path $base 'app') `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $err
    $p.WaitForExit()
    Start-Sleep -Seconds 30
}
