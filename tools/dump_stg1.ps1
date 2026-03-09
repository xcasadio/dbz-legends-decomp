$f = [IO.File]::ReadAllBytes('d:\development\repo\dbz-legends-decomp\data\STG\STG1MD.B')
$meshTableOff = [BitConverter]::ToUInt32($f, 0)
$partListOff  = [BitConverter]::ToUInt32($f, 4)
Write-Host "File size: $($f.Length) bytes"
Write-Host "MeshTableOff:   $meshTableOff"
Write-Host "ParticleListOff: $partListOff"
Write-Host ""
Write-Host "Mesh entries (non-zero offsets):"
for ($i = 0; $i -lt 16; $i++) {
    $e  = $meshTableOff + $i * 8
    $mo = [BitConverter]::ToUInt32($f, $e)
    $ty = [BitConverter]::ToUInt32($f, $e + 4)
    if ($mo -gt 0) {
        Write-Host "  [$i]: fileOffset=$mo  type=$ty"
    }
}
Write-Host ""
$pc = [BitConverter]::ToUInt16($f, [int]$partListOff)
Write-Host "Particle count: $pc"
$pl = [int]$partListOff + 2
$limit = [Math]::Min([int]$pc, 40)
for ($i = 0; $i -lt $limit; $i++) {
    $mi = [BitConverter]::ToInt16($f, $pl)
    $px = [BitConverter]::ToInt16($f, $pl + 2)
    $pz = [BitConverter]::ToInt16($f, $pl + 4)
    Write-Host "  [$i]: mesh=$mi  pos=($px, $pz)"
    $pl += 6
}
if ($pc -gt 40) {
    $rem = $pc - 40
    Write-Host "  ...($rem more particles)"
}
