# =============================================
#   Bat bore.pub tunnel va cap nhat config
#   Khong can tai khoan, khong bao gio fail port
#   Yeu cau: bore.exe phai co trong thu muc nay
#   Chay: .\start-internet-bore.ps1
# =============================================

$scriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientConfig = Join-Path $scriptDir "DrawClient\config.ini"
$serverConfig = Join-Path $scriptDir "DrawServer\config.ini"
$borePath     = Join-Path $scriptDir "bore.exe"
$remoteHost   = "bore.pub"

if (-not (Test-Path $borePath)) {
    Write-Host "[ERROR] Khong tim thay bore.exe trong: $scriptDir" -ForegroundColor Red
    Write-Host "        Tai tai: https://github.com/ekzhang/bore/releases" -ForegroundColor Yellow
    Write-Host "        Giai nen bore.exe vao cung thu muc voi script nay." -ForegroundColor Yellow
    pause
    exit 1
}

# Ham chay bore va doi lay port duoc cap phat
function Start-BoreTunnel {
    param([int]$LocalPort, [string]$Label)

    Write-Host "[*] Mo tunnel $Label (localhost:$LocalPort -> bore.pub:?)..." -ForegroundColor Cyan

    # Chay bore trong background job, capture stdout
    $job = Start-Job -ScriptBlock {
        param($exe, $lp)
        & $exe local $lp --to bore.pub 2>&1
    } -ArgumentList $borePath, $LocalPort

    # Cho toi khi bore in ra port (toi da 15 giay)
    $timeout = 15
    $allocatedPort = $null

    for ($i = 0; $i -lt $timeout; $i++) {
        Start-Sleep -Seconds 1
        $output = Receive-Job $job -Keep

        foreach ($line in $output) {
            if ($line -match "bore\.pub:(\d+)") {
                $allocatedPort = [int]$Matches[1]
                break
            }
        }
        if ($allocatedPort) { break }
    }

    if ($allocatedPort) {
        Write-Host "[OK] $Label : bore.pub:$allocatedPort -> localhost:$LocalPort" -ForegroundColor Green
        return @{ Port = $allocatedPort; Job = $job }
    } else {
        Write-Host "[FAIL] Khong nhan duoc port tu bore.pub cho $Label" -ForegroundColor Red
        $output = Receive-Job $job
        $output | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkYellow }
        Stop-Job $job
        return $null
    }
}

# --- Mo tunnel MasterServer (port 5274) ---
$masterResult = Start-BoreTunnel -LocalPort 5274 -Label "MasterServer"
if (-not $masterResult) { exit 1 }
$masterPort = $masterResult.Port

# --- Mo tunnel DrawServer (port 6001) ---
$drawResult = Start-BoreTunnel -LocalPort 6001 -Label "DrawServer"
if (-not $drawResult) { exit 1 }
$drawPort = $drawResult.Port

# --- Cap nhat DrawClient/config.ini ---
$clientContent = @"
# =============================================
#   Cau hinh DrawClient - CHE DO INTERNET (bore.pub)
#   KHONG can build lai sau khi sua file nay
# =============================================

[Server]
MasterServerIp=$remoteHost
MasterServerPort=$masterPort
"@
Set-Content -Path $clientConfig -Value $clientContent -Encoding UTF8
Write-Host "[OK] DrawClient\config.ini -> ${remoteHost}:${masterPort}" -ForegroundColor Green

# --- Cap nhat DrawServer/config.ini ---
$serverRaw = Get-Content $serverConfig -Raw -Encoding UTF8
$serverRaw = $serverRaw -replace "(?m)^NodeIp=.*",  "NodeIp=$remoteHost"
$serverRaw = $serverRaw -replace "(?m)^NodePort=.*", "NodePort=$drawPort"
Set-Content -Path $serverConfig -Value $serverRaw -Encoding UTF8
Write-Host "[OK] DrawServer\config.ini -> NodeIp=$remoteHost NodePort=$drawPort" -ForegroundColor Green

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " CHE DO INTERNET - bore.pub" -ForegroundColor Green
Write-Host "  MasterServer : ${remoteHost}:${masterPort}" -ForegroundColor Green
Write-Host "  DrawServer   : ${remoteHost}:${drawPort}" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host ">>> Gui cho ban be (ho cap nhat DrawClient\config.ini):" -ForegroundColor Yellow
Write-Host "    MasterServerIp=$remoteHost" -ForegroundColor White
Write-Host "    MasterServerPort=$masterPort" -ForegroundColor White
Write-Host ""
Write-Host "Sau khi tunnel OK: Restart DrawServer de dang ky NodeIp/Port moi len Master." -ForegroundColor Cyan
Write-Host ""
Write-Host "CAC TUNNEL DANG CHAY TRONG BACKGROUND." -ForegroundColor Magenta
Write-Host "Giu cua so nay mo. Dong cua so nay se tat tat ca tunnel." -ForegroundColor Magenta
Write-Host ""

# Giu script chay de background jobs (tunnel) khong bi kill
# Nhan Ctrl+C de dung
try {
    Write-Host "Nhan Ctrl+C de tat tunnel..." -ForegroundColor DarkGray
    while ($true) { Start-Sleep -Seconds 30 }
}
finally {
    Write-Host "Dang tat tunnel..." -ForegroundColor Yellow
    Stop-Job $masterResult.Job, $drawResult.Job -ErrorAction SilentlyContinue
    Remove-Job $masterResult.Job, $drawResult.Job -ErrorAction SilentlyContinue
    Write-Host "Da tat tat ca tunnel." -ForegroundColor Green
}
