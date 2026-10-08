# Stops WatchingYou completely: the task, the keeper script it runs, and the site itself.
# (Ending the task alone leaves the keeper running, and it would start the site again.)
Stop-ScheduledTask -TaskName WatchingYou -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -match 'run-site\.ps1' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep 1
Get-Process MovieRental.Host -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "WatchingYou stopped. Start it again with: schtasks /run /tn WatchingYou"
