# FN OS Assistant Launcher
# 1. Kill leftover processes
# 2. Clean build cache & temp files
# 3. Build and run
# 4. After close, cleanup

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectDir

Write-Host "=== Fnos Assistant Launcher ===" -ForegroundColor Cyan

# ---- 1. Kill leftover processes ----
Write-Host "[1/4] Cleaning leftover processes..." -ForegroundColor Yellow
$killed = Get-Process -Name "FnosAssistant" -ErrorAction SilentlyContinue
if ($killed) {
    $killed | ForEach-Object { 
        Write-Host "  Killing PID $($_.Id)..." -ForegroundColor Gray
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue 
    }
    Start-Sleep -Milliseconds 500
    Write-Host "  Killed $($killed.Count) leftover process(es)." -ForegroundColor Green
} else {
    Write-Host "  No leftover processes found." -ForegroundColor Gray
}

# Also kill any orphan dotnet processes running FnosAssistant
$orphans = Get-WmiObject Win32_Process -Filter "Name='dotnet.exe'" | 
    Where-Object { $_.CommandLine -match 'FnosAssistant' } |
    ForEach-Object { $_.ProcessId }
if ($orphans) {
    $orphans | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
    Write-Host "  Killed $($orphans.Count) orphan dotnet process(es)." -ForegroundColor Green
}

# ---- 2. Clean build cache & temp ----
Write-Host "[2/4] Cleaning build cache & temp..." -ForegroundColor Yellow
$pathsToClean = @(
    "bin", "obj",
    "$env:LOCALAPPDATA\Temp\FnosAssistant",
    "$env:TEMP\FnosAssistant"
)
foreach ($path in $pathsToClean) {
    $fullPath = if ([System.IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $projectDir $path }
    if (Test-Path $fullPath) {
        Remove-Item -Recurse -Force $fullPath -ErrorAction SilentlyContinue
        Write-Host "  Removed: $fullPath" -ForegroundColor Gray
    }
}
Write-Host "  Cleanup complete." -ForegroundColor Green

# ---- 3. Build ----
Write-Host "[3/4] Building..." -ForegroundColor Yellow
$buildResult = dotnet build -v q 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED:" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host "  Build succeeded." -ForegroundColor Green

# ---- 4. Run ----
Write-Host "[4/4] Launching application..." -ForegroundColor Yellow
Write-Host ""
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$projectDir`"" -PassThru -NoNewWindow
Write-Host "  App running (PID: $($proc.Id)). Waiting for exit..." -ForegroundColor Green

# Wait for the app to close
$proc.WaitForExit()
Write-Host ""

# ---- Cleanup after close ----
Write-Host "=== Cleaning up after exit ===" -ForegroundColor Cyan

# Kill any remaining sub-processes
Get-Process -Name "FnosAssistant" -ErrorAction SilentlyContinue | 
    Stop-Process -Force -ErrorAction SilentlyContinue

# Clean temp again
foreach ($path in $pathsToClean) {
    $fullPath = if ([System.IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $projectDir $path }
    if (Test-Path $fullPath) {
        Remove-Item -Recurse -Force $fullPath -ErrorAction SilentlyContinue
        Write-Host "  Removed: $fullPath" -ForegroundColor Gray
    }
}

# Clean .NET temp
$dotnetTemp = Join-Path $projectDir ".dotnet"
if (Test-Path $dotnetTemp) {
    Remove-Item -Recurse -Force $dotnetTemp -ErrorAction SilentlyContinue
}

Write-Host "Cleanup done. Exiting." -ForegroundColor Green
