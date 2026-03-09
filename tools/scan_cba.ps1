$sizes = @(44, 52, 60, 76, 36, 44, 60, 80)
$uvOff = @(32, 40, 48, 64, -1, -1, -1, -1)

foreach ($n in 1..5) {
    $mf = "d:\development\repo\dbz-legends-decomp\data\STG\STG${n}MD.B"
    $tf = "d:\development\repo\dbz-legends-decomp\data\STG\STG${n}TX.B"
    if (!(Test-Path $mf)) { continue }
    $f = [IO.File]::ReadAllBytes($mf)
    $meshTableOff = [BitConverter]::ToUInt32($f, 0)

    # Collect all unique CBA/TSB combos across the stage
    $combos = @{}
    for ($mi = 0; $mi -lt 16; $mi++) {
        $e  = $meshTableOff + $mi * 8
        $mo = [BitConverter]::ToUInt32($f, $e)
        if ($mo -le 4 -or $mo -ge $f.Length) { continue }
        $rto = [BitConverter]::ToUInt32($f, $mo - 4)
        if ($rto -eq 0 -or $rto -gt 0x100000) { continue }
        $rtb = $mo + $rto - 4
        $pc  = [BitConverter]::ToUInt32($f, $rtb)
        if ($pc -eq 0 -or $pc -gt 64) { continue }

        for ($pi = 0; $pi -lt $pc; $pi++) {
            $po = [BitConverter]::ToUInt32($f, $rtb + 4 + $pi * 4)
            $pa = $rtb + $po
            $ns = [BitConverter]::ToUInt32($f, $pa)
            if ($ns -eq 0 -or $ns -gt 32) { continue }
            $c  = $pa + 4

            for ($si = 0; $si -lt $ns; $si++) {
                $cnt = [BitConverter]::ToUInt16($f, $c)
                $ty  = [BitConverter]::ToUInt16($f, $c + 2) % 8
                $c  += 4
                $sz  = $sizes[$ty]

                if ($uvOff[$ty] -ge 0) {
                    for ($pr = 0; $pr -lt $cnt; $pr++) {
                        $ub  = $c + $pr * $sz + $uvOff[$ty]
                        $cba = [BitConverter]::ToUInt16($f, $ub)
                        $tsb = [BitConverter]::ToUInt16($f, $ub + 2)
                        $cbaHex = '{0:X4}' -f $cba
                        $tsbHex = '{0:X4}' -f $tsb
                        $key = "CBA=0x${cbaHex} TSB=0x${tsbHex}"
                        if (-not $combos.ContainsKey($key)) { $combos[$key] = 0 }
                        $combos[$key]++
                    }
                }
                $c += $cnt * $sz
            }
        }
    }

    # Also show which CLUT entries are in the TX file
    $tfd = [IO.File]::ReadAllBytes($tf)
    $tc  = [BitConverter]::ToUInt32($tfd, 0)
    Write-Host "STG$n TX CLUT entries (isClut=1):"
    for ($i = 0; $i -lt $tc; $i++) {
        $te = 4 + $i * 28
        $isClut = [BitConverter]::ToUInt32($tfd, $te + 24)
        if ($isClut -ne 1) { continue }
        $vx = [BitConverter]::ToUInt32($tfd, $te + 8)
        $vy = [BitConverter]::ToUInt32($tfd, $te + 12)
        $w  = [BitConverter]::ToUInt32($tfd, $te + 16)
        Write-Host "  TX[$i]: CLUT at VRAM ($vx, $vy) w=$w colors (CBA_X=$($vx / 16))"
    }
    Write-Host ""

    Write-Host "STG$n unique CBA/TSB combinations:"
    $combos.GetEnumerator() | Sort-Object Name | ForEach-Object {
        $parts = $_.Name -split ' '
        $cbaStr = $parts[0] -replace 'CBA=0x',''
        $cbaVal = [Convert]::ToInt32($cbaStr, 16)
        $clutX  = ($cbaVal -band 0x3F) * 16
        $clutY  = ($cbaVal -shr 6) -band 0x1FF
        Write-Host "  $($_.Name) (count=$($_.Value)) -> CLUT VRAM: ($clutX, $clutY)"
    }
    Write-Host ""
}
