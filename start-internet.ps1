# =============================================
#   Bat serveo.net voi port NGAU NHIEN
#   Tranh xung dot port voi nguoi dung khac
#   Chay: .\start-internet.ps1
# =============================================

$scriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientConfig = Join-Path $scriptDir "DrawClient\config.ini"
$serverConfig = Join-Path $scriptDir "DrawServer\config.ini"
$remoteHost   = "serveo.net"

# Chon 2 port ngau nhien trong khoang 20000-59999
$masterPort = Get-Random -Minimum 20000 -Maximum 59999
$drawPort   = Get-Random -Minimum 20000 -Maximum 59999
while ($drawPort -eq $masterPort) { $drawPort = Get-Random -Minimum 20000 -Maximum 59999 }

Write-Host ""
Write-Host "[*] Port duoc chon ngau nhien lan nay:" -ForegroundColor Cyan
Write-Host "    MasterServer : serveo.net:$masterPort  ->  localhost:5274" -ForegroundColor White
Write-Host "    DrawServer   : serveo.net:$drawPort  ->  localhost:6001" -ForegroundColor White
Write-Host ""

# --- Mo tunnel MasterServer ---
Write-Host "[*] Mo tunnel MasterServer..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", `
    "Write-Host '[TUNNEL] MasterServer: serveo.net:$masterPort -> localhost:5274' -ForegroundColor Cyan; ssh -o StrictHostKeyChecking=accept-new -R ${masterPort}:localhost:5274 serveo.net" `
    -WindowStyle Normal

Start-Sleep -Seconds 2

# --- Mo tunnel DrawServer ---
Write-Host "[*] Mo tunnel DrawServer..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", `
    "Write-Host '[TUNNEL] DrawServer: serveo.net:$drawPort -> localhost:6001' -ForegroundColor Cyan; ssh -o StrictHostKeyChecking=accept-new -R ${drawPort}:localhost:6001 serveo.net" `
    -WindowStyle Normal

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
Write-Host "[OK] DrawClient\config.ini -> ${remoteHost}:${masterPort}" -ForegroundColor Green

# --- Cap nhat DrawServer/config.ini ---
$serverRaw = Get-Content $serverConfig -Raw
$serverRaw = $serverRaw -replace "(?m)^NodeIp=.*",  "NodeIp=$remoteHost"
$serverRaw = $serverRaw -replace "(?m)^NodePort=.*", "NodePort=$drawPort"
Set-Content -Path $serverConfig -Value $serverRaw -Encoding UTF8
Write-Host "[OK] DrawServer\config.ini -> NodeIp=$remoteHost NodePort=$drawPort" -ForegroundColor Green

Write-Host ""
Write-Host "=====================================" -ForegroundColor Green
Write-Host " CHE DO INTERNET - serveo.net" -ForegroundColor Green
Write-Host "  MasterServer : ${remoteHost}:${masterPort}" -ForegroundColor Green
Write-Host "  DrawServer   : ${remoteHost}:${drawPort}" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green
Write-Host ""
Write-Host ">>> Gui cho ban be thong tin nay (ho cap nhat DrawClient\config.ini):" -ForegroundColor Yellow
Write-Host "    MasterServerIp=$remoteHost" -ForegroundColor White
Write-Host "    MasterServerPort=$masterPort" -ForegroundColor White
Write-Host ""
Write-Host "Neu cua so tunnel bao 'remote port forwarding failed', chay lai script" -ForegroundColor Yellow
Write-Host "nay de lay port ngau nhien moi (hiem khi xay ra voi range 20000-59999)." -ForegroundColor Yellow
Write-Host ""
Write-Host "Sau khi tunnel OK: Restart DrawServer de no dang ky NodeIp/Port moi len Master." -ForegroundColor Cyan
