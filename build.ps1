$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $rootDir "dist"
$assetsDir = Join-Path $rootDir "assets"
$icoPath = Join-Path $assetsDir "app.ico"

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

# Generate a proper Windows .ico file using .NET System.Drawing
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(64, 64)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, 64, 64)),
    [System.Drawing.Color]::FromArgb(147, 51, 234),
    [System.Drawing.Color]::FromArgb(107, 33, 168),
    45.0
)
$g.FillRectangle($brush, 4, 4, 56, 56)
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 6)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$pts = @(
    (New-Object System.Drawing.PointF(16, 33)),
    (New-Object System.Drawing.PointF(28, 45)),
    (New-Object System.Drawing.PointF(49, 20))
)
$g.DrawLines($pen, $pts)
$g.Dispose()

# Write PNG-encoded ICO file
$msPng = New-Object System.IO.MemoryStream
$bmp.Save($msPng, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $msPng.ToArray()
$msPng.Dispose()
$bmp.Dispose()

$fsIco = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fsIco)
# ICONDIR header
$bw.Write([UInt16]0) # Reserved
$bw.Write([UInt16]1) # Type: 1 = ICO
$bw.Write([UInt16]1) # Count: 1 image
# ICONDIRENTRY
$bw.Write([Byte]64)  # Width
$bw.Write([Byte]64)  # Height
$bw.Write([Byte]0)   # ColorCount
$bw.Write([Byte]0)   # Reserved
$bw.Write([UInt16]1) # Planes
$bw.Write([UInt16]32)# BitCount
$bw.Write([UInt32]$pngBytes.Length) # BytesInRes
$bw.Write([UInt32]22) # ImageOffset (6 + 16)
$bw.Write($pngBytes)
$bw.Flush()
$bw.Close()
$fsIco.Dispose()

Write-Host "Icon generated at $icoPath"

$csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$srcFile = Join-Path $rootDir "src\ContasAPagar.cs"
$appExe = Join-Path $distDir "ContasAPagar.exe"
$instaladorExe = Join-Path $distDir "instalador.exe"
$zipPath = Join-Path $distDir "Instalador-ContasAPagar.zip"

Write-Host "Compiling ContasAPagar.exe..."
& $csc /nologo /target:winexe /optimize+ /out:$appExe `
    /win32icon:$icoPath `
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

Copy-Item $appExe $instaladorExe -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Start-Sleep -Milliseconds 300
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipStream = [System.IO.File]::Create($zipPath)
$archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
$entry = $archive.CreateEntry("instalador.exe", [System.IO.Compression.CompressionLevel]::Optimal)
$entryStream = $entry.Open()
$inStream = [System.IO.File]::Open($instaladorExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
$inStream.CopyTo($entryStream)
$inStream.Close()
$entryStream.Close()
$archive.Dispose()
$zipStream.Close()

Write-Host "Build completed successfully!"
Get-ChildItem $distDir
