"""Croise la table de repartition C# avec les 51 pointeurs lus dans data/VS.EXE.

Une erreur d'ordre dans la table ne produit ni erreur de compilation ni banc rouge:
elle appellerait simplement le mauvais gestionnaire. C'est exactement la classe de
defaut que ce portage a payee neuf fois, alors on la mesure.
"""
import io
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

EXE = r"D:/development/repo/dbz-legends-decomp/data/VS.EXE"
VS = r"D:/development/repo/dbz-legends-decomp/custom-tools/DbzLegendsAnalyser/DbzLegendsRemaster/VS_EXE"

# PS-EXE: t_addr = 0x80020000, l'image commence a l'offset 0x800.
raw = io.open(EXE, "rb").read()


def at(addr, n):
    off = 0x800 + (addr - 0x80020000)
    return raw[off:off + n]


ptrs = []
blob = at(0x800822F4, 51 * 4)
for i in range(51):
    ptrs.append(int.from_bytes(blob[i * 4:i * 4 + 4], "little"))

names = []
nblob = at(0x800823C0, 50 * 16)
for i in range(50):
    ent = nblob[i * 16:i * 16 + 16]
    names.append(ent.split(b"\x00")[0].decode("ascii", "replace").strip())

# Adresse de chaque methode, depuis l'annotation GHIDRA qui la precede dans son fichier.
addr_of = {}
for fam in ("Appearance", "Control", "Effects", "Mesh", "Sound", "Transform"):
    path = VS + "/AnimCmd" + fam + ".cs"
    lines = io.open(path, encoding="utf-8-sig").read().splitlines()
    # On remonte depuis chaque methode jusqu a la plus proche annotation GHIDRA qui la
    # precede, dans une fenetre de 80 lignes: les blocs de commentaire de ce depot sont
    # longs, et l annotation peut s ecrire "no symbol @ 0x...", donc pas un mot unique.
    for i, line in enumerate(lines):
        m = re.match(r"\s*internal static int (\w+)\s*\(", line)
        if not m:
            continue
        for j in range(i - 1, max(-1, i - 80), -1):
            g = re.search(r"//\s*GHIDRA:.*?@\s*(0x[0-9A-Fa-f]{8})", lines[j])
            if g:
                addr_of["AnimCmd" + fam + "." + m.group(1)] = int(g.group(1), 16)
                break

# L'ordre des appels dans la table C#.
src = io.open(VS + "/AnimVmInterpreter.cs", encoding="utf-8-sig").read()
tbl = src.split("g_animStreamDispatchTable =", 1)[1].split("};", 1)[0]
calls = re.findall(r"=>\s*(AnimCmd\w+\.\w+)\s*\(", tbl)

print("pointeurs lus dans data/VS.EXE : %d" % len(ptrs))
print("noms lus                       : %d" % len(names))
print("entrees de la table C#         : %d" % len(calls))
print()

bad = 0
for i, call in enumerate(calls):
    want = ptrs[i]
    got = addr_of.get(call)
    nm = names[i] if i < len(names) else "(sans nom)"
    if got is None:
        print("  [%2d] %-14s %-44s ANNOTATION INTROUVABLE" % (i, nm, call))
        bad += 1
    elif got != want:
        print("  [%2d] %-14s %-44s C# 0x%08X != binaire 0x%08X" % (i, nm, call, got, want))
        bad += 1

print("desaccords : %d sur %d" % (bad, len(calls)))
if bad == 0 and len(calls) == 51:
    print("TABLE CONFORME : les 51 emplacements appellent le gestionnaire que le binaire designe.")
