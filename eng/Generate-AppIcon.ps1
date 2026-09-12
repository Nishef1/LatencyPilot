param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.Clear([System.Drawing.Color]::Transparent)

            $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 112, 140))
            try {
                $inset = [Math]::Max(1.0, $size * 0.04)
                $graphics.FillEllipse($background, $inset, $inset, $size - (2 * $inset), $size - (2 * $inset))
            }
            finally {
                $background.Dispose()
            }

            $penWidth = [Math]::Max(1.5, $size * 0.085)
            $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penWidth)
            try {
                $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
                $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

                $points = [System.Drawing.PointF[]]@(
                    [System.Drawing.PointF]::new($size * 0.17, $size * 0.55),
                    [System.Drawing.PointF]::new($size * 0.31, $size * 0.55),
                    [System.Drawing.PointF]::new($size * 0.40, $size * 0.29),
                    [System.Drawing.PointF]::new($size * 0.53, $size * 0.73),
                    [System.Drawing.PointF]::new($size * 0.64, $size * 0.42),
                    [System.Drawing.PointF]::new($size * 0.82, $size * 0.42)
                )
                $graphics.DrawLines($pen, $points)
            }
            finally {
                $pen.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$file = [System.IO.File]::Create($OutputPath)
try {
    $writer = New-Object System.IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $data = $images[$index]
            $dimension = if ($size -ge 256) { [byte]0 } else { [byte]$size }

            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$data.Length)
            $writer.Write([uint32]$offset)
            $offset += $data.Length
        }

        foreach ($data in $images) {
            $writer.Write($data)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $file.Dispose()
}

Write-Host "Generated LatencyPilot icon: $OutputPath"
