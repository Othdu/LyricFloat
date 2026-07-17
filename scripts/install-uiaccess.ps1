# ============================================================================
#  LyricFloat uiAccess installer
#  Run from an ADMIN PowerShell, from the repo root, AFTER `dotnet publish`:
#      powershell -ExecutionPolicy Bypass -File scripts\install-uiaccess.ps1
#
#  Windows only honors uiAccess=true when the exe is (a) Authenticode-signed
#  with a certificate the machine trusts and (b) running from a secure path.
#  This script: creates a self-signed code-signing cert (once), trusts it,
#  copies the published build to Program Files, and signs the exe there.
# ============================================================================

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: run this from an elevated (Administrator) PowerShell." -ForegroundColor Red
    exit 1
}

# Layout 1: release zip (exe next to this script). Layout 2/3: dev repo.
$publishDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path "$publishDir\LyricFloat.exe")) {
    $publishDir = "src\LyricFloat\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
}
if (-not (Test-Path "$publishDir\LyricFloat.exe")) {
    $publishDir = "src\LyricFloat\bin\Release\net8.0-windows10.0.19041.0\publish"
}
if (-not (Test-Path "$publishDir\LyricFloat.exe")) {
    Write-Host "ERROR: LyricFloat.exe not found next to this script or in the repo publish folder." -ForegroundColor Red
    exit 1
}

$installDir = "$env:ProgramFiles\LyricFloat"
$certName   = "CN=LyricFloat Dev"

# --- 1. Self-signed code-signing certificate (created once, reused after) ---
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $certName } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating self-signed code-signing certificate..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $certName `
            -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
}

# --- 2. Trust it machine-wide (required for uiAccess) ---
Write-Host "Trusting the certificate (LocalMachine Root + TrustedPublisher)..." -ForegroundColor Cyan
$tmpCer = "$env:TEMP\LyricFloatDev.cer"
Export-Certificate -Cert $cert -FilePath $tmpCer | Out-Null
Import-Certificate -FilePath $tmpCer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Import-Certificate -FilePath $tmpCer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher | Out-Null
Remove-Item $tmpCer

# --- 3. Stop a running instance, copy to Program Files ---
Get-Process LyricFloat -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Write-Host "Installing to $installDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item "$publishDir\*" $installDir -Recurse -Force

# --- 4. Sign the installed exe ---
Write-Host "Signing LyricFloat.exe ..." -ForegroundColor Cyan
$sig = Set-AuthenticodeSignature -FilePath "$installDir\LyricFloat.exe" -Certificate $cert
if ($sig.Status -ne "Valid") {
    Write-Host "ERROR: signing failed: $($sig.StatusMessage)" -ForegroundColor Red
    exit 1
}

# --- 5. Desktop shortcut ---
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut("$([Environment]::GetFolderPath('Desktop'))\LyricFloat.lnk")
$sc.TargetPath = "$installDir\LyricFloat.exe"
$sc.Save()

Write-Host ""
Write-Host "Done. Launch LyricFloat from the desktop shortcut (or Program Files)." -ForegroundColor Green
Write-Host "IMPORTANT: always run THIS copy - uiAccess is ignored for the exe in your dev folder." -ForegroundColor Yellow
