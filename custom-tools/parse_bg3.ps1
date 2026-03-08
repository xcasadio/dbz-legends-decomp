# Parse the 80 background instances from INT_ARRAY_80087d94
# These are at offsets [6..6+80*3] as int32 = bytes [24..24+960]
# Each instance = 3 × int32 (sign-extended int16): posX, posY, posZ
# Instance data starts at 0x80087dac (= 0x80087d94 + 24)

# First decode the sprite template header (first 24 bytes = 6 int32)
Write-Host "=== Sprite Template Header at 0x80087d94 ==="
Write-Host ""
Write-Host "[0] count = 1 (1 sprite part per template)"
Write-Host ""
Write-Host "Sprite part data (bytes 4-23):"
Write-Host "  u0 = 0x00 (0)"
Write-Host "  v0 = 0x00 (0)"
Write-Host "  xCenter = 0x54 - 0x80 = -44 (half of 88)"
Write-Host "  yCenter = 0x6c - 0x80 = -20 (half of 40)"
Write-Host "  clutOffset = 0x7f00"
Write-Host "  tpage = 0x2e => tpX=14, tpY=1, 4bpp, semi=01"
Write-Host "  width = 88 pixels (0x58)"
Write-Host "  height = 40 pixels (0x28)"
Write-Host "  rotZ = 0, scaleX = 0x1000 (1.0), scaleY = 0x1000 (1.0)"
Write-Host ""
Write-Host "=== Alt Sprite Template at 0x80087d7c (for non-stage-2/6) ==="
Write-Host "  u0=8, v0=0, 96x32 px, center=(-48,-16), same tpage"
Write-Host ""

# Now read the 80 instances from emulator-provided raw data
# From the hex dump at 0x80087dac (960 bytes):
$rawHex = @"
a8fdffff 9cffffff a8fdffff f8f8ffff 38ffffff 70feffff
e0fcffff 70feffff 50fbffff c0f9ffff a8fdffff 18fcffff
d8f5ffff 9cffffff e0fcffff 88faffff d4feffff 88faffff
70feffff a8fdffff f8f8ffff 28f1ffff 44fdffff 70feffff
b8f2ffff 0cfeffff 18fcffff 70feffff 9cffffff b8f2ffff
18fcffff 7cfcffff d8f5ffff 50fbffff 0cfeffff f0f1ffff
f8f8ffff 38ffffff a0f6ffff f8f8ffff a8fdffff 80f3ffff
68f7ffff 7cfcffff f8f8ffff 48f4ffff 70feffff c0f9ffff
f0f1ffff d4feffff 68f7ffff 10f5ffff e0fcffff 10f5ffff
10f5ffff 70feffff f0f1ffff 28f1ffff 38ffffff f0f1ffff
a8fdffff 9cffffff
58020000 f8f8ffff 38ffffff 90010000 e0fcffff 70feffff
b0040000 c0f9ffff a8fdffff e8030000 d8f5ffff 9cffffff
20030000 88faffff d4feffff 78050000 70feffff a8fdffff
08070000 28f1ffff 44fdffff 90010000 b8f2ffff 0cfeffff
e8030000 70feffff 9cffffff 480d0000 18fcffff 7cfcffff
280a0000 50fbffff 0cfeffff 100e0000 f8f8ffff 38ffffff
60090000 f8f8ffff a8fdffff 800c0000 68f7ffff 7cfcffff
08070000 48f4ffff 70feffff 40060000 f0f1ffff d4feffff
98080000 10f5ffff e0fcffff f00a0000 10f5ffff 70feffff
100e0000 28f1ffff 38ffffff 100e0000
58020000 9cffffff a8fdffff 08070000 38ffffff 70feffff
20030000 70feffff 50fbffff 40060000 a8fdffff 18fcffff
280a0000 9cffffff e0fcffff 78050000 d4feffff 88faffff
90010000 a8fdffff f8f8ffff d80e0000 44fdffff 70feffff
480d0000 0cfeffff 18fcffff 90010000 9cffffff b8f2ffff
e8030000 7cfcffff d8f5ffff b0040000 0cfeffff f0f1ffff
08070000 38ffffff a0f6ffff 08070000 a8fdffff 80f3ffff
98080000 7cfcffff f8f8ffff b80b0000 70feffff c0f9ffff
100e0000 d4feffff 68f7ffff f00a0000 e0fcffff 10f5ffff
f00a0000 70feffff f0f1ffff d80e0000 38ffffff f0f1ffff
58020000 9cffffff 58020000 08070000 38ffffff 90010000
20030000 70feffff b0040000 40060000 a8fdffff e8030000
280a0000 9cffffff 20030000 78050000 d4feffff 78050000
90010000 a8fdffff 08070000 d80e0000 44fdffff 90010000
480d0000 0cfeffff e8030000 90010000 9cffffff 480d0000
e8030000 7cfcffff 280a0000 b0040000 0cfeffff 100e0000
08070000 38ffffff 60090000 08070000 a8fdffff 800c0000
98080000 7cfcffff 08070000 b80b0000 70feffff 40060000
100e0000 d4feffff 98080000 f00a0000 e0fcffff f00a0000
f00a0000 70feffff 100e0000 d80e0000 38ffffff 100e0000
"@

# Parse hex into int32 array
$words = ($rawHex -replace "`n"," " -replace "\s+"," ").Trim().Split(" ") |
    Where-Object { $_ -ne "" } |
    ForEach-Object {
        $bytes = [byte[]]::new(4)
        for ($b = 0; $b -lt 4; $b++) { $bytes[$b] = [Convert]::ToByte($_.Substring($b*2, 2), 16) }
        [BitConverter]::ToInt32($bytes, 0)
    }

Write-Host "Total int32 values parsed: $($words.Count) (expected 240 = 80 * 3)"
Write-Host ""
Write-Host "=== 80 Background Instances ==="
Write-Host ("{0,4} {1,8} {2,8} {3,8}" -f "#", "posX", "posY", "posZ")
Write-Host ("-" * 36)

$xVals = @()
$yVals = @()
$zVals = @()

for ($i = 0; $i -lt 80; $i++) {
    $x = $words[$i*3]
    $y = $words[$i*3+1]
    $z = $words[$i*3+2]
    Write-Host ("{0,4} {1,8} {2,8} {3,8}" -f $i, $x, $y, $z)
    $xVals += $x; $yVals += $y; $zVals += $z
}

$xs = $xVals | Sort-Object -Unique
$ys = $yVals | Sort-Object -Unique
$zs = $zVals | Sort-Object -Unique

Write-Host ""
Write-Host "=== Ranges ==="
Write-Host "X: [$($xs[0]) .. $($xs[-1])] ($($xs.Count) unique)"
Write-Host "Y: [$($ys[0]) .. $($ys[-1])] ($($ys.Count) unique)"
Write-Host "Z: [$($zs[0]) .. $($zs[-1])] ($($zs.Count) unique)"
Write-Host ""
Write-Host "Unique Y: $($ys -join ', ')"
Write-Host "Unique X: $($xs -join ', ')"
Write-Host "Unique Z: $($zs -join ', ')"

# Check pattern: instances 0-19, 20-39, 40-59, 60-79
Write-Host ""
Write-Host "=== Pattern check: Z values by group ==="
for ($g = 0; $g -lt 4; $g++) {
    $gzs = @()
    for ($i = $g*20; $i -lt ($g+1)*20; $i++) {
        $gzs += $words[$i*3+2]
    }
    $gzUniq = $gzs | Sort-Object -Unique
    Write-Host "Group $g (inst $($g*20)..$($g*20+19)): Z range [$($gzUniq[0])..$($gzUniq[-1])]"
}
