@echo off
rem ---------------------------------------------------------------------------
rem  WatchingYou - opens the site.
rem  The site itself is kept running by the Windows task "WatchingYou" (starts at sign-in,
rem  restarts itself if it stops). This just makes sure the task is running, then opens
rem  the browser.   https://localhost:7139
rem
rem    Stop the site:        schtasks /end /tn WatchingYou
rem    Remove the autostart: schtasks /delete /tn WatchingYou /f
rem ---------------------------------------------------------------------------
schtasks /run /tn WatchingYou >nul 2>&1
timeout /t 3 /nobreak >nul
start "" https://localhost:7139
