
# Parse INT_ARRAY_80087d94 from the raw memory dump
# Layout: header[6] then 80 instances × 3 int32 (x, y, z)

$base = 0x80087d94

# Read from the SLPS EXE file — compute file offset
# PSX-EXE header = 0x800 bytes, text base = 0x80010000
$exePath = "d:\development\repo\dbz-legends-decomp\data\SLPS_003.55"
$exe = [System.IO.File]::ReadAllBytes($exePath)
$fileOffset = $base - 0x80010000 + 0x800

Write-Host "=== Header (indices 0..5) ==="
for ($i = 0; $i -lt 6; $i++) {
    $off = $fileOffset + $i * 4
    $val = [BitConverter]::ToInt32($exe, $off)
    $hex = "0x" + ([uint32]$val).ToString("X8")
    Write-Host "  [$i] = $val ($hex)"
}

Write-Host "`n=== 80 Background Instances (indices 6..245) ==="
Write-Host ("{0,4} {1,8} {2,8} {3,8}" -f "#", "X", "Y", "Z")
Write-Host ("-" * 36)

$instances = @()
for ($i = 0; $i -lt 80; $i++) {
    $idx = 6 + $i * 3
    $off = $fileOffset + $idx * 4
    $x = [BitConverter]::ToInt32($exe, $off)
    $y = [BitConverter]::ToInt32($exe, $off + 4)
    $z = [BitConverter]::ToInt32($exe, $off + 8)
    Write-Host ("{0,4} {1,8} {2,8} {3,8}" -f $i, $x, $y, $z)
    $instances += [pscustomobject]@{Idx=$i; X=$x; Y=$y; Z=$z}
}

# Analyze ranges
$xs = $instances | ForEach-Object { $_.X }
$ys = $instances | ForEach-Object { $_.Y }
$zs = $instances | ForEach-Object { $_.Z }

Write-Host "`n=== Value Ranges ==="
Write-Host "X: min=$($xs | Measure-Object -Minimum | Select -ExpandProperty Minimum) max=$($xs | Measure-Object -Maximum | Select -ExpandProperty Maximum)"
Write-Host "Y: min=$($ys | Measure-Object -Minimum | Select -ExpandProperty Minimum) max=$($ys | Measure-Object -Maximum | Select -ExpandProperty Maximum)"
Write-Host "Z: min=$($zs | Measure-Object -Minimum | Select -ExpandProperty Minimum) max=$($zs | Measure-Object -Maximum | Select -ExpandProperty Maximum)"

# Check unique values
$uniqueX = $xs | Sort-Object -Unique
$uniqueY = $ys | Sort-Object -Unique
$uniqueZ = $zs | Sort-Object -Unique
Write-Host "`nUnique X values ($($uniqueX.Count)): $($uniqueX -join ', ')"
Write-Host "Unique Y values ($($uniqueY.Count)): $($uniqueY -join ', ')"
Write-Host "Unique Z values ($($uniqueZ.Count)): $($uniqueZ -join ', ')"

# Check which header values might be rendering parameters
Write-Host "`n=== Header analysis ==="
$h0 = [BitConverter]::ToInt32($exe, $fileOffset + 0)
$h1 = [BitConverter]::ToInt32($exe, $fileOffset + 4)
$h2 = [BitConverter]::ToInt32($exe, $fileOffset + 8)
$h3 = [BitConverter]::ToInt32($exe, $fileOffset + 12)
$h4 = [BitConverter]::ToInt32($exe, $fileOffset + 16)
$h5 = [BitConverter]::ToInt32($exe, $fileOffset + 20)

Write-Host "h[0]=$h0 (count or mode?)"
Write-Host "h[1]=$h1 (as hex: 0x$([uint32]$h1).ToString('X8'))"
Write-Host "h[2]=$h2 (0x$([uint32]$h2).ToString('X4'))"
Write-Host "h[3]=$h3 h[4]=$h4"
Write-Host "h[5]=$h5 (0x$([uint32]$h5).ToString('X8'))"

# If h1 is a pointer: 0x6c540000 -> nope, let's check as LE
Write-Host "`nh[1] as unsigned: $([uint32]$h1) -> hex 0x$([uint32]$h1).ToString('X8')"
# Check if h[2..5] could be tpage/clut info
Write-Host "h[2] low byte=$($h2 -band 0xFF) = clut or tpage?"
Write-Host "h[3]=$h3 h[4]=$h4 -> sprite dimensions? ${h3}x${h4}"

