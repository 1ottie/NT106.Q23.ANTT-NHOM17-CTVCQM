# =============================================
#   Khoi phuc config.ini ve che do LAN
#   Chay: .\restore-lan.ps1
# =============================================

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientConfig = Join-Path $scriptDir "DrawClient\config.ini"
$serverConfig = Join-Path $scriptDir "DrawServer\config.ini"

# Đổi IP LAN ở đây nếu cần
$lanIp = "192.168.2.6"

# --- DrawClient ---
$clientContent = @"
# =============================================
#   Cau hinh DrawClient
#   Doi IP khi chuyen sang mang LAN khac
#   KHONG can build lai sau khi sua file nay
# =============================================

[Server]
MasterServerIp=$lanIp
MasterServerPort=5274
"@
Set-Content -Path $clientConfig -Value $clientContent -Encoding UTF8
Write-Host "[OK] DrawClient\config.ini -> $lanIp : 5274" -ForegroundColor Green

# --- DrawServer ---
$serverRaw = Get-Content $serverConfig -Raw
$serverRaw = $serverRaw -replace "(?m)^NodeIp=.*",   "NodeIp=$lanIp"
$serverRaw = $serverRaw -replace "(?m)^NodePort=.*",  "NodePort=6001"
$serverRaw = $serverRaw -replace "(?m)^MasterServerIp=.*", "MasterServerIp=$lanIp"
Set-Content -Path $serverConfig -Value $serverRaw -Encoding UTF8
Write-Host "[OK] DrawServer\config.ini  -> NodeIp=$lanIp NodePort=6001" -ForegroundColor Green

Write-Host ""
Write-Host "Da khoi phuc ve LAN ($lanIp). Rebuild lai DrawServer de cap nhat NodeIp." -ForegroundColor Cyan
