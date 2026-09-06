"""Balayage des declarations en double, y compris les `const`, dans un overlay et entre overlays.

Une declaration dupliquee ne casse ni la compilation ni un banc: les deux copies vivent
tranquillement, et seule celle que le code lit compte. C'est ce qui a produit douze symboles
dupliques et sept types divergents en tranche 1. Ce balayage les voit, et il voit aussi les
`const`, que le premier balayage manquait.

Usage: python custom-tools/scripts/check_duplicate_symbols.py [--strict]
       --strict sort 1 des qu'un doublon intra-overlay ou un type divergent subsiste,
       pour servir de garde-fou avant commit.
"""
import io, os, re, sys
from collections import defaultdict
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
# Derive du fichier lui-meme: un chemin absolu code en dur ne marche que sur une machine, et le
# depot en a deja paye le prix ailleurs.
ROOT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "custom-tools", "DbzLegendsAnalyser", "DbzLegendsRemaster")
FOLDERS = ["VS_EXE", "TITLE_EXE", "SELECT_EXE", "MOVIE_EXE", "SLPS_003_55", "."]
DECL = re.compile(r'^\s*(?:internal|private|public)\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?([\w\[\]<>.]+)\s+((?:DAT_|PTR_|RAM_|g_|_DAT_)\w+)\s*(?:=|;)')

decls = defaultdict(list)   # symbole -> [(overlay, fichier, ligne, type, const?)]
for folder in FOLDERS:
    d = os.path.join(ROOT, folder)
    for name in sorted(os.listdir(d)):
        if not name.endswith(".cs"):
            continue
        for i, line in enumerate(io.open(os.path.join(d, name), encoding="utf-8-sig"), 1):
            m = DECL.match(line)
            if m:
                decls[m.group(2)].append((folder, name, i, m.group(1), "const" in line))

def show(title, items):
    print("=== %s : %d" % (title, len(items)))
    divergent = 0
    for k, v in sorted(items):
        types = {t for _, _, _, t, _ in v}
        flag = "  <-- TYPES DIVERGENTS" if len(types) > 1 else ""
        if len(types) > 1:
            divergent += 1
        print("  %-22s %s%s" % (k, " | ".join("%s/%s:%d %s%s" % (f, n, l, t, " const" if c else "") for f, n, l, t, c in v), flag))
    return len(items), divergent

intra = [(k, v) for k, v in decls.items() if len({(f, n) for f, n, _, _, _ in v if f == "VS_EXE"}) > 1]
n_intra, div_intra = show("VS_EXE : symbole declare dans plus d un fichier (static OU const)",
                          [(k, [x for x in v if x[0] == "VS_EXE"]) for k, v in intra])
cross = [(k, v) for k, v in decls.items() if "VS_EXE" in {f for f, *_ in v} and len({f for f, *_ in v}) > 1]
n_cross, div_cross = show("VS_EXE : symbole aussi declare dans un AUTRE overlay ou a la racine", cross)

print()
print("intra-overlay: %d (dont %d a types divergents) | inter-overlay: %d (dont %d divergents)"
      % (n_intra, div_intra, n_cross, div_cross))

# Le mode strict ne gate QUE sur l'intra-overlay et les types divergents. Les doublons
# inter-overlay sont un fait connu du portage (chaque overlay est lie separement et porte ses
# propres globales); les gater bloquerait tout commit sans rien apprendre.
if "--strict" in sys.argv:
    fail = n_intra + div_cross
    print("STRICT: %d probleme(s) bloquant(s)" % fail)
    sys.exit(1 if fail else 0)
