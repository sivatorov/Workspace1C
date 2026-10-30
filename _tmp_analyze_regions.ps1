$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing

function FindRegions([string]$path, [string]$label, [int]$minBright, [int]$maxR, [int]$minG) {
    $full = Join-Path (Get-Location) $path
    $bmp = [System.Drawing.Bitmap]::new($full)
    $w = $bmp.Width
    $h = $bmp.Height
    Write-Host ('--- {0} ({1}x{2}) ---' -f $label, $w, $h)
    # find connected components of "interesting" pixels (sampled every 1 px)
    $minX = $w; $minY = $h; $maxX = -1; $maxY = -1
    $count = 0
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bright = ($c.R + $c.G + $c.B) / 3
            $isGreen = ($c.G -gt 90) -and ($c.G -gt ($c.R + 40)) -and ($c.G -gt ($c.B + 40))
            if (($bright -ge $minBright) -or $isGreen) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
                $count++
            }
        }
    }
    if ($count -gt 0) {
        Write-Host ('  region: X {0}..{1}, Y {2}..{3}, w={4}, h={5}, px={6}' -f $minX, $maxX, $minY, $maxY, ($maxX - $minX + 1), ($maxY - $minY + 1), $count)
    } else {
        Write-Host '  no bright/green pixels'
    }
    $bmp.Dispose()
}

# Light/bright regions (white artifacts) - brightness >= 170
FindRegions '_issue26_shot1.png' 'Shot1 main bright' 170 220 0
FindRegions '_issue26_shot2.png' 'Shot2 bright' 170 220 0
FindRegions '_issue26_shot3.png' 'Shot3 bright' 170 220 0
FindRegions '_issue26_shot4.png' 'Shot4 bright' 170 220 0
FindRegions '_issue26_shot5.png' 'Shot5 bright' 170 220 0
FindRegions '_issue26_shot6.png' 'Shot6 bright' 170 220 0

# Green regions - G dominant
Write-Host ''
FindRegions '_issue26_shot4.png' 'Shot4 green' 500 90 1
