# Stops WatchingYou completely: the task, the keeper scripts it runs, the site itself and the
# phone app's dev server. (Ending the task alone leaves the keepers running, and they would
# start everything again.)
Stop-ScheduledTask -TaskName WatchingYou -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -match 'run-(site|mobile)\.ps1' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep 1
Get-Process MovieRental.Host -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='node.exe'" |
    Where-Object { $_.CommandLine -match 'vite\.mobile\.config' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Write-Host "WatchingYou stopped. Start it again with: schtasks /run /tn WatchingYou"
