 param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'ARM',
    [string]$OutDir = 'out'
)

$ErrorActionPreference = 'Stop'

# --- 1. Подписывающий сертификат -------------------------------------------------
# Для повторяемых сборок используется секрет MONO_SIGN_PFX_B64 + MONO_SIGN_PASS.
# Публичный сертификат (signing/monochrome.cer) коммится в репозиторий:
# пользователи ставят .cer один раз, обновления остаются доверенными.

$signDir  = Join-Path $OutDir 'signing'
$packageDir = Join-Path $OutDir 'appx'

if (-not (Test-Path $signDir))  { New-Item -ItemType Directory -Path $signDir | Out-Null }
if (-not (Test-Path $packageDir)) { New-Item -ItemType Directory -Path $packageDir | Out-Null }

$pfxPath = Join-Path $signDir 'monochrome.pfx'

if ($env:MONO_SIGN_PFX_B64) {
    Write-Host 'Подпись: используется сертификат из секрета MONO_SIGN_PFX_B64.'
    $b64 = $env:MONO_SIGN_PFX_B64 -replace '\s', ''
    [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($b64))
}
else {
    Write-Host 'Подпись: секрет не задан, генерируем временный самоподписанный сертификат.'
    $notAfter = (Get-Date).AddYears(10)
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject 'CN=Monochrome' `
        -FriendlyName 'Monochrome code signing' `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -KeySpec Signature `
        -NotAfter $notAfter
    $pass = ConvertTo-SecureString -String 'monochrome' -Force -AsPlainText
    [IO.File]::WriteAllBytes($pfxPath, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, 'monochrome'))
}

$certPassword = if ($env:MONO_SIGN_PASS) { $env:MONO_SIGN_PASS } else { 'monochrome' }

# --- 2. Версия пакета ------------------------------------------------------------
# Прописываем версию прямо в Package.appxmanifest (Identity/@Version):
# по умолчанию 1.0.0.0; для тега v1.2.3 => 1.2.3.0

$packageVersion = '1.0.0.0'
if ($env:MONO_VERSION) {
    $v = $env:MONO_VERSION.TrimStart('vV')
    $parts = $v.Split('.')
    if ($parts.Count -lt 3) { $v = (($parts + @('0','0','0'))[0..2] -join '.') }
    $packageVersion = "$v.0"
}
Write-Host "Версия пакета: $packageVersion"

$manifestPath = Join-Path $PSScriptRoot '..\src\Monochrome\Package.appxmanifest'
$manifest = Get-Content -Raw $manifestPath
$pattern  = '(<Identity\s+Name="[^"]+"\s+Publisher="[^"]+"\s+Version=")[^"]+(")'
$manifest = [regex]::Replace($manifest, $pattern, ('${1}' + $packageVersion + '${2}'))
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8 -NoNewline

# --- 3. Сборка -------------------------------------------------------------------

$msbuild = &"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" `
    | Select-Object -First 1

if (-not $msbuild) { throw 'MSBuild не найден (vswhere).' }
Write-Host "MSBuild: $msbuild"

& $msbuild (Join-Path $PSScriptRoot '..\src\Monochrome.sln') `
    -m `
    -t:Restore,Build `
    -p:"Configuration=$Configuration" `
    -p:"Platform=$Platform" `
    -p:'AppxPackageSigningEnabled=true' `
    -p:"PackageCertificatePassword=$certPassword" `
    -p:"PackageCertificateKeyFile=$pfxPath" `
    -p:'UapAppxPackageBuildMode=SideloadOnly' `
    -p:'AppxBundle=never' `
    -p:'GenerateAppxPackageOnBuild=true' `
    -p:"AppxPackageDir=$packageDir" `
    -p:'UseDotNetNativeToolchain=false'
if ($LASTEXITCODE -ne 0) { throw "Сборка упала (код $LASTEXITCODE)." }

# --- 4. Копируем результат в папку release ---------------------------------------

$releaseDir = Join-Path $OutDir 'release'
if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir | Out-Null }

Get-ChildItem -Path $packageDir -Recurse -Include '*.appx','*.msix','*.appxbundle','*.cer' |
    Copy-Item -Destination $releaseDir

Copy-Item (Join-Path $PSScriptRoot '..\signing\monochrome.cer') $releaseDir

Write-Host 'Готово. Артефакты:'
Get-ChildItem $releaseDir | Select-Object Name, Length | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Host $_ }
