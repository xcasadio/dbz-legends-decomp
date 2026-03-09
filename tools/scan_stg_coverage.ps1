$sizes = @(44, 52, 60, 76, 36, 44, 60, 80)
$uvOff = @(32, 40, 48, 64, -1, -1, -1, -1)

foreach ($n in 1..5) {
    $mf = "d:\development\repo\dbz-legends-decomp\data\STG\STG${n}MD.B"
    $tf2 = "d:\development\repo\dbz-legends-decomp\data\STG\STG${n}TX.B"
    if (!(Test-Path $mf)) { continue }
    $tf = [IO.File]::ReadAllBytes($tf2)
    $tc = [BitConverter]::ToUInt32($tf, 0)
    $txE = @()
    for ($i = 0; $i -lt $tc; $i++) {
        $e = 4 + $i * 28
        if ([BitConverter]::ToUInt32($tf, $e + 24) -ne 0) { continue }
        $txE += [PSCustomObject]@{
            tpX = [int]([BitConverter]::ToUInt32($tf, $e + 8) / 64)
            vY  = [BitConverter]::ToUInt32($tf, $e + 12)
            h   = [BitConverter]::ToUInt32($tf, $e + 20)
        }
    }

    $f = [IO.File]::ReadAllBytes($mf)
    $miss = 0
    $tot = 0
    $missDetails = @()

    for ($mi = 0; $mi -lt 16; $mi++) {
        $mo = [BitConverter]::ToUInt32($f, 8 + $mi * 8)
        if ($mo -lt 4) { continue }
        $rto = [BitConverter]::ToUInt32($f, $mo - 4)
        if ($rto -eq 0 -or $rto -gt 100000) { continue }
        $rtb = $mo + $rto - 4
        $pc = [BitConverter]::ToUInt32($f, $rtb)

        for ($pi = 0; $pi -lt $pc; $pi++) {
            $po = [BitConverter]::ToUInt32($f, $rtb + 4 + $pi * 4)
            $pa = $rtb + $po
            $ns = [BitConverter]::ToUInt32($f, $pa)
            $c = $pa + 4

            for ($si = 0; $si -lt $ns; $si++) {
                $cnt = [BitConverter]::ToUInt16($f, $c)
                $ty  = [BitConverter]::ToUInt16($f, $c + 2) % 8
                $c += 4
                $sz = $sizes[$ty]

                if ($uvOff[$ty] -ge 0) {
                    for ($pr = 0; $pr -lt $cnt; $pr++) {
                        $ub = $c + $pr * $sz + $uvOff[$ty]
                        $tsb = [BitConverter]::ToUInt16($f, $ub + 2)
                        $tpx = $tsb -band 0xF
                        $tpy = ($tsb -shr 4) -band 1
                        $v0  = $f[$ub + 5]
                        $absY = $tpy * 256 + $v0
                        $tot++
                        $ok = ($txE | Where-Object { $_.tpX -eq $tpx -and $_.vY -le $absY -and $absY -lt ($_.vY + $_.h) } | Select-Object -First 1)
                        if (-not $ok) {
                            $miss++
                            $tsbHex = '{0:X4}' -f $tsb
                            $missDetails += "  mesh${mi} part${pi} sec${si} prim${pr}: tsb=0x${tsbHex} tpX=$tpx tpY=$tpy v0=$v0 absY=$absY"
                        }
                    }
                }
                $c += $cnt * $sz
            }
        }
    }

    Write-Host "STG$n : miss=$miss / tot=$tot"
    if ($miss -gt 0 -and $miss -le 20) {
        $missDetails | ForEach-Object { Write-Host $_ }
    } elseif ($miss -gt 20) {
        Write-Host "  (first 20 misses:)"
        $missDetails | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
    }
}
