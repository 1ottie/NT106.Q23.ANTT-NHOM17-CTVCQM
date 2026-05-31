# =============================================
#   Bat serveo.net va tu dong cap nhat config
#   Chay: .\start-internet.ps1
# =============================================

$scriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientConfig = Join-Path $scriptDir "DrawClient\config.ini"
$serverConfig = Join-Path $scriptDir "DrawServer\config.ini"

$remoteHost   = "serveo.net"
$masterPort   = 5274
$drawPort     = 6001

# --- Mo tunnel MasterServer (port 5274) ---
Write-Host "[*] Mo tunnel MasterServer (port $masterPort)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "ssh -R ${masterPort}:localhost:${masterPort} serveo.net" -WindowStyle Normal

Start-Sleep -Seconds 2

# --- Mo tunnel DrawServer (port 6001) ---
Write-Host "[*] Mo tunnel DrawServer (port $drawPort)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "ssh -R ${drawPort}:localhost:${drawPort} serveo.net" -WindowStyle Normal

# --- Cap nhat DrawClient/config.ini ---
$clientContent = @"
# =============================================
#   Cau hinh DrawClient - CHE DO INTERNET
#   KHONG can build lai sau khi sua file nay
# =============================================

[Server]
MasterServerIp=$remoteHost
MasterServerPort=$masterPort
"@
Set-Content -Path $clientConfig -Value $clientContent -Encoding UTF8
Write-Host "[OK] DrawClient\config.ini -> $remoteHost : $masterPort" -ForegroundColor Green

# --- Cap nhat DrawServer/config.ini ---
$serverRaw = Get-Content $serverConfig -Raw
$serverRaw = $serverRaw -replace "(?m)^NodeIp=.*",  "NodeIp=$remoteHost"
$serverRaw = $serverRaw -replace "(?m)^NodePort=.*", "NodePort=$drawPort"
Set-Content -Path $serverConfig -Value $serverRaw -Encoding UTF8
Write-Host "[OK] DrawServer\config.ini  -> NodeIp=$remoteHost NodePort=$drawPort" -ForegroundColor Green

Write-Host ""
Write-Host "=====================================" -ForegroundColor Green
Write-Host " CHE DO INTERNET - serveo.net" -ForegroundColor Green
Write-Host "  MasterServer : $remoteHost : $masterPort" -ForegroundColor Green
Write-Host "  DrawServer   : $remoteHost : $drawPort" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green
Write-Host ""
Write-Host "Gui cho ban be (ho dien vao DrawClient\config.ini cua ho):" -ForegroundColor Yellow
Write-Host "  MasterServerIp=$remoteHost" -ForegroundColor White
Write-Host "  MasterServerPort=$masterPort" -ForegroundColor White
Write-Host ""
Write-Host "Luu y: Rebuild DrawServer roi chay lai de dang ky NodeIp moi." -ForegroundColor Cyan
