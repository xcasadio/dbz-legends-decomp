$sizes = @(44, 52, 60, 76, 36, 44, 60, 80)
$typeNames = @('FT3','FT4','GT3','GT4','F3','F4','G3','G4')

foreach ($n in 1..5) {
    $mf = "d:\development\repo\dbz-legends-decomp\data\STG\STG${n}MD.B"
    if (!(Test-Path $mf)) { continue }
    $f = [IO.File]::ReadAllBytes($mf)
    $meshTableOff = [BitConverter]::ToUInt32($f, 0)
    $hasUntex = $false

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
            $c = $pa + 4

            for ($si = 0; $si -lt $ns; $si++) {
                $cnt = [BitConverter]::ToUInt16($f, $c)
                $ty  = [BitConverter]::ToUInt16($f, $c + 2) % 8
                $c += 4
                if ($ty -ge 4) {
                    Write-Host "STG$n mesh[$mi] part[$pi] sec[$si]: type=$($typeNames[$ty]) count=$cnt  <-- UNTEXTURED"
                    $hasUntex = $true
                }
                $c += $cnt * $sizes[$ty]
            }
        }
    }
    if (-not $hasUntex) {
        Write-Host "STG$n : no untextured sections found"
    }
}
