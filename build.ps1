$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $rootDir "dist"
$assetsDir = Join-Path $rootDir "assets"
$icoPath = Join-Path $assetsDir "app.ico"
$manifestPath = Join-Path $rootDir "src\app.manifest"
$certFile = Join-Path $distDir "Certificado-Seguranca.cer"
$liberarCmd = Join-Path $distDir "1-Liberar-Instalador.cmd"

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

# 1. Generate a proper Windows .ico file using .NET System.Drawing
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(64, 64)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# Deep Slate background
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, 64, 64)),
    [System.Drawing.Color]::FromArgb(15, 23, 42),
    [System.Drawing.Color]::FromArgb(30, 41, 59),
    45.0
)
$g.FillRectangle($bgBrush, 4, 4, 56, 56)

# Emerald top bar
$accentBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(4, 4, 56, 16)),
    [System.Drawing.Color]::FromArgb(16, 185, 129),
    [System.Drawing.Color]::FromArgb(5, 150, 105),
    0.0
)
$g.FillRectangle($accentBrush, 4, 4, 56, 14)

# Grid subtle lines
$gridBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(71, 85, 105))
$g.FillRectangle($gridBrush, 12, 26, 18, 4)
$g.FillRectangle($gridBrush, 12, 35, 14, 4)
$g.FillRectangle($gridBrush, 12, 44, 12, 4)

# Emerald check badge
$badgeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(16, 185, 129))
$g.FillEllipse($badgeBrush, 28, 26, 26, 26)

$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 4.2)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$pts = @(
    (New-Object System.Drawing.PointF(34, 39)),
    (New-Object System.Drawing.PointF(39, 44)),
    (New-Object System.Drawing.PointF(48, 34))
)
$g.DrawLines($pen, $pts)
$g.Dispose()

$msPng = New-Object System.IO.MemoryStream
$bmp.Save($msPng, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $msPng.ToArray()
$msPng.Dispose()
$bmp.Dispose()

$fsIco = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fsIco)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]1)
$bw.Write([Byte]64)
$bw.Write([Byte]64)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]32)
$bw.Write([UInt32]$pngBytes.Length)
$bw.Write([UInt32]22)
$bw.Write($pngBytes)
$bw.Flush()
$bw.Close()
$fsIco.Dispose()

Write-Host "Icon generated at $icoPath"

# 2. Compile with embedded UAC/OS manifest (/win32manifest) and deterministic PE header
$csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$srcFile = Join-Path $rootDir "src\ContasAPagar.cs"
$appExe = Join-Path $distDir "ContasAPagar.exe"
$instaladorExe = Join-Path $distDir "instalador.exe"
$zipPath = Join-Path $distDir "Instalador-ContasAPagar.zip"

Write-Host "Compiling ContasAPagar.exe with Windows Application Manifest..."
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /utf8output /out:$appExe `
    /win32icon:$icoPath `
    /win32manifest:$manifestPath `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    /r:System.Runtime.Serialization.dll `
    /r:System.Xml.dll `
    /r:System.IO.Compression.dll `
    /r:System.IO.Compression.FileSystem.dll `
    /r:Microsoft.CSharp.dll `
    $srcFile

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

# 3. Sign executables with Authenticode Certificate (CodeSigning)
$certSubject = "CN=Contas a Pagar - Planilha Inteligente"
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating Authenticode Code Signing Certificate..."
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $certSubject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10)
}

Export-Certificate -Cert $cert -FilePath $certFile -Force | Out-Null

Write-Host "Signing ContasAPagar.exe with Authenticode SHA256..."
Set-AuthenticodeSignature -FilePath $appExe -Certificate $cert -HashAlgorithm SHA256 | Out-Null

Copy-Item $appExe $instaladorExe -Force

# 4. Create helper script to unblock Mark-of-the-Web (Zone.Identifier), trust certificate in CurrentUser, and launch instalador.exe
$cmdLines = @(
    "@echo off",
    "chcp 65001 >nul",
    "title Instalador - Contas a Pagar",
    "echo Preparando instalador seguro...",
    "powershell -NoProfile -ExecutionPolicy Bypass -Command ""Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File -ErrorAction SilentlyContinue; if (Test-Path '%~dp0Certificado-Seguranca.cer') { Import-Certificate -FilePath '%~dp0Certificado-Seguranca.cer' -CertStoreLocation 'Cert:\CurrentUser\Root' -ErrorAction SilentlyContinue | Out-Null; Import-Certificate -FilePath '%~dp0Certificado-Seguranca.cer' -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher' -ErrorAction SilentlyContinue | Out-Null }""",
    "start """" ""%~dp0instalador.exe"" --install"
)
[System.IO.File]::WriteAllLines($liberarCmd, $cmdLines, [System.Text.Encoding]::ASCII)

# 5. Package Instalador-ContasAPagar.zip
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Start-Sleep -Milliseconds 300
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipStream = [System.IO.File]::Create($zipPath)
$archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)

foreach ($fileToZip in @($instaladorExe, $liberarCmd, $certFile)) {
    $entryName = [System.IO.Path]::GetFileName($fileToZip)
    $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $inStream = [System.IO.File]::Open($fileToZip, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $inStream.CopyTo($entryStream)
    $inStream.Close()
    $entryStream.Close()
}

$archive.Dispose()
$zipStream.Close()

Write-Host "Build completed successfully!"
Get-ChildItem $distDir
