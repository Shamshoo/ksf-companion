# Renders icon.svg to PNGs at every size Windows uses and packs them into app.ico.
# Uses the Microsoft Edge that ships with Windows (headless), so nothing extra needs installing.
# Run from this folder:  powershell -ExecutionPolicy Bypass -File build-icon.ps1

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "Microsoft Edge not found" }

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$work = Join-Path ([IO.Path]::GetTempPath()) ("ksfc-icon-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null

# icon-small.svg is the same scene without the far ramp, stars and lane lines, with a bolder trail,
# so the tiny sizes (taskbar, title bar) stay crisp.
Copy-Item (Join-Path $here "icon.svg") (Join-Path $work "icon.svg")
Copy-Item (Join-Path $here "icon-small.svg") (Join-Path $work "icon-small.svg")

$pngs = @()
foreach ($size in $sizes) {
    $source = if ($size -le 24) { "icon-small.svg" } else { "icon.svg" }
    $html = Join-Path $work "render-$size.html"
    Set-Content $html "<!doctype html><html><body style='margin:0;background:transparent'><img src='$source' width='$size' height='$size' style='display:block'></body></html>" -Encoding UTF8
    $shot = Join-Path $work "shot-$size.png"
    $edgeProfile = Join-Path $work "edge-profile"
    $edgeArgs = @("--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1", "--default-background-color=00000000",
                  "--user-data-dir=`"$edgeProfile`"", "--window-size=512,512", "--screenshot=`"$shot`"", "`"file:///$($html.Replace('\', '/'))`"")
    $process = Start-Process -FilePath $edge -ArgumentList $edgeArgs -PassThru -WindowStyle Hidden
    $process | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
    if (-not (Test-Path $shot)) { throw "Edge didn't render size $size" }

    # The page is 512x512; the icon sits in the top-left corner.
    $full = [System.Drawing.Bitmap]::FromFile($shot)
    $icon = $full.Clone((New-Object System.Drawing.Rectangle 0, 0, $size, $size), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $full.Dispose()
    $png = Join-Path $here "icon-$size.png"
    $icon.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $icon.Dispose()
    $pngs += $png
}

# .ico = small header + one directory entry per size + the PNG images themselves.
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter($out)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
$blobs = @()
for ($i = 0; $i -lt $pngs.Count; $i++) {
    $bytes = [IO.File]::ReadAllBytes($pngs[$i])
    $blobs += , $bytes
    $s = $sizes[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$bytes.Length); $w.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($b in $blobs) { $w.Write($b) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $here "app.ico"), $out.ToArray())
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
"app.ico written with sizes: $($sizes -join ', ')"
