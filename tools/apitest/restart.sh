#!/bin/bash
# Stops the test server, rebuilds, starts it again (e-mail/SMS to the log) and waits until healthy.
powershell -NoProfile -Command "Get-Process -Name 'MovieRental.Host' -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:\$false" >/dev/null 2>&1
sleep 2
cd /c/wy/WatchingYou
nohup dotnet run --project src/MovieRental.Host --launch-profile "MovieRental.Host (no browser)" -p:SkipClientBuild=true -- \
  --Notifications:Email:Provider=Console --Notifications:Sms:Provider=Console > /c/wy/apitest/server.log 2>&1 &
for i in $(seq 1 90); do
  if curl -sk -o /dev/null -w "%{http_code}" https://localhost:7139/api/health 2>/dev/null | grep -q 200; then echo "server up"; exit 0; fi
  sleep 2
done
echo "server did not start"; tail -20 /c/wy/apitest/server.log; exit 1
