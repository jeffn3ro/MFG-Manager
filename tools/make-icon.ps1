<#
    Draws the MFG Manager logo: stacked frames. The solid gold-to-lime frame in front is a real frame,
    and the outlined frames behind it are the generated ones.
    Saves:
      app.ico   - every size Windows uses (taskbar, title bar, Explorer, desktop)
      logo.png  - 256 px, shown in the app and used as the Linux icon
    Sizes under 32 px use two frames instead of three, because three turn to mush at 16 px.
    logo.svg, next to these files, is the same design for Linux desktops.

    Usage:  powershell -File tools\make-icon.ps1 -OutFolder src\MfgManager.App\Assets
#>
param([Parameter(Mandatory)][string]$OutFolder)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$IcoSizes   = 16, 20, 24, 32, 40, 48, 64, 256
$Background = [Windows.Media.Color]::FromRgb(0x10, 0x11, 0x14)
$Gold       = [Windows.Media.Color]::FromRgb(0xF2, 0xC2, 0x30)
$Lime       = [Windows.Media.Color]::FromRgb(0xA3, 0xE6, 0x35)
# Faded lime as solid colours. See-through ones would leave a darker patch where frames overlap.
$LimeFar    = New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(0x48, 0x62, 0x21))
$LimeNear   = New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(0x77, 0xA6, 0x2B))

# All sizes below are on a 256 x 256 grid; the drawing is then scaled to the real size.
function New-IconPng([int]$Size) {
    $backgroundBrush = New-Object Windows.Media.SolidColorBrush $Background
    $front = New-Object Windows.Media.LinearGradientBrush $Gold, $Lime, (New-Object Windows.Point 0, 0), (New-Object Windows.Point 1, 1)

    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($Size / 256.0), ($Size / 256.0)))
    $dc.DrawRoundedRectangle($backgroundBrush, $null, (New-Object Windows.Rect 0, 0, 256, 256), 52, 52)

    if ($Size -lt 32) {
        $pen = New-Object Windows.Media.Pen $LimeNear, 22
        $dc.DrawRoundedRectangle($backgroundBrush, $pen, (New-Object Windows.Rect 92, 40, 124, 124), 18, 18)
        $dc.DrawRoundedRectangle($front, $null, (New-Object Windows.Rect 36, 88, 132, 132), 20, 20)
    }
    else {
        $back = New-Object Windows.Media.Pen $LimeFar, 14
        $middle = New-Object Windows.Media.Pen $LimeNear, 14
        $dc.DrawRoundedRectangle($null, $back, (New-Object Windows.Rect 104, 44, 116, 116), 18, 18)
        $dc.DrawRoundedRectangle($backgroundBrush, $middle, (New-Object Windows.Rect 70, 70, 116, 116), 18, 18)
        $dc.DrawRoundedRectangle($front, $null, (New-Object Windows.Rect 36, 96, 116, 116), 18, 18)
    }

    $dc.Pop()
    $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $Size, $Size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

# An .ico file is a small table of contents followed by one PNG per size.
function Write-Ico([string]$Path, [int[]]$Sizes) {
    $images = foreach ($s in $Sizes) { , (New-IconPng $s) }
    $out = New-Object IO.BinaryWriter ([IO.File]::Create($Path))
    try {
        $out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$Sizes.Count)
        $offset = 6 + 16 * $Sizes.Count
        for ($i = 0; $i -lt $Sizes.Count; $i++) {
            $side = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }   # 0 means 256
            $out.Write([byte]$side); $out.Write([byte]$side); $out.Write([byte]0); $out.Write([byte]0)
            $out.Write([uint16]1); $out.Write([uint16]32)
            $out.Write([uint32]$images[$i].Length); $out.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($image in $images) { $out.Write([byte[]]$image) }
    }
    finally { $out.Dispose() }
}

New-Item -ItemType Directory -Force $OutFolder | Out-Null
$OutFolder = (Resolve-Path $OutFolder).Path
Write-Ico (Join-Path $OutFolder 'app.ico') $IcoSizes
[IO.File]::WriteAllBytes((Join-Path $OutFolder 'logo.png'), (New-IconPng 256))
"Saved app.ico ($($IcoSizes -join ', ') px) and logo.png to $OutFolder"
