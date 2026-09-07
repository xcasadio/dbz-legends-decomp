"""Balayage des declarations en double, y compris les `const`, dans un overlay et entre overlays.

Une declaration dupliquee ne casse ni la compilation ni un banc: les deux copies vivent
tranquillement, et seule celle que le code lit compte. C'est ce qui a produit douze symboles
dupliques et sept types divergents en tranche 1. Ce balayage les voit, et il voit aussi les
`const`, que le premier balayage manquait.

Deuxieme passe: STOCKAGE CONTENU DANS UNE REGION. La passe ci-dessus est indexee par NOM, donc
elle ne peut pas voir un scalaire dont l'adresse tombe DANS une `RamRegion` declaree ailleurs sous
un autre nom. C'est passe: AnimVmInterpreter tenait `private static int DAT_800990c8` pendant que
BattleScene modelisait 0x800990C0..0x800990D7 en une seule region. Deux copies des memes octets, et
une largeur fausse par-dessus (l'image dit `lbu`/`sb`, le scalaire etait un `int`, et un ecriture
32 bits a +0x08 aurait ecrase +0x09..+0x0B).

CE QUE CETTE PASSE NE VOIT PAS, dit ici parce qu'un vert doit etre lisible:
  * les symboles dont le nom ne commence pas par DAT_/PTR_/RAM_/g_/_DAT_. Le premier temoin
    negatif ecrit pour cette passe s'appelait TEMOIN_NEGATIF et n'a PAS declenche -- le
    vérificateur etait vacuous et le temoin l'a montre. Renomme DAT_800990d0, il declenche. Le
    filtre est volontaire (il cible les globales translitterees), mais il est une limite reelle.
  * les regions dont la base ou la taille ne se resout pas. Elles sont listees comme angles morts
    au lieu d'etre comptees comme propres, et le compte final le dit.
  * les noms qualifies dont le dernier segment est ambigu (`POLY_FT4Ref.Size` se reduit a `Size`,
    qui existe dans plusieurs classes). Quand les candidats divergent, la region devient un angle
    mort au lieu d'etre dimensionnee au hasard -- une premiere version prenait la premiere valeur
    trouvee et a signale deux symboles comme contenus dans des bornes ou ils ne sont pas.

Usage: python custom-tools/scripts/check_duplicate_symbols.py [--strict]
       --strict sort 1 sur les doublons de STOCKAGE, pas sur les doublons d'ADRESSE.

La distinction est celle que le port a lui-meme etablie (VS_EXE/AnimVmInterpreter.cs:32):
deux `const int` portant la meme adresse ne sont PAS un defaut -- ils resolvent tous deux
a travers PsxRam vers la meme region, une adresse n'est pas un stockage. Deux `byte[]`, ou
un `byte[]` et un scalaire, sur la meme adresse SONT un defaut: deux copies des memes
octets, qui divergent des que l'une est ecrite. Un vérificateur qui hurle sur les premiers
apprend a ignorer les seconds.
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

# CE QUI COMPTE EST L'ADRESSE, PAS LE NOM. Deux overlays lies separement portent leurs propres
# globales a des adresses differentes, et le portage VS a emprunte les noms de TITLE pour la
# lisibilite: g_TaskListHead existe dans les deux, a deux adresses, dans deux namespaces. Ce ne
# sont pas des doublons, ce sont des homonymes, et les signaler comme des defauts apprend a
# ignorer le vérificateur. Un doublon reel, c'est DEUX DECLARATIONS SUR UNE MEME ADRESSE.
GHIDRA = re.compile(r'//\s*GHIDRA:.*?@\s*(0x[0-9A-Fa-f]{6,8})')
INIT = re.compile(r'=\s*(?:unchecked\(\(int\)\s*)?(0x[0-9A-Fa-f]{6,8})')

decls = defaultdict(list)   # symbole -> [(overlay, fichier, ligne, type, const?, adresse)]

# LA PASSE QUI MANQUAIT, et le defaut qu'elle aurait attrape est reel. Ce balayage est indexe par
# NOM: il compare les adresses de declarations qui portent le meme nom. Il ne peut donc pas voir un
# scalaire dont l'adresse tombe DANS une region declaree ailleurs sous un autre nom -- et c'est
# exactement ce qui s'est produit. AnimVmInterpreter tenait `private static int DAT_800990c8` et
# `private static byte DAT_800990d4` pendant que BattleScene modelisait 0x800990C0..0x800990D7 en une
# seule `RamRegion`. Deux copies des memes octets, invisibles a ce vérificateur, avec en prime une
# largeur fausse: l'image dit `lbu`/`sb`, le scalaire etait un `int`, et un ecriture 32 bits a +0x08
# aurait ecrase +0x09, +0x0A et +0x0B.
#
# La regle appliquee est celle que le portage a deja etablie: une ADRESSE n'est pas un STOCKAGE.
# Un `const int` qui pointe dans une region est correct et attendu -- c'est comme cela qu'on
# adresse un champ. Un scalaire non-const, ou un second `byte[]`, dans les bornes d'une region est
# une seconde copie et un defaut.
REGION = re.compile(r'RamRegion\(\s*([^,]+?)\s*,\s*([^,)]+?)\s*[,)]')
CONST = re.compile(r'\bconst\s+(?:int|uint)\s+(\w+)\s*=\s*([^;]+);')
CLASS = re.compile(r'\b(?:class|struct|record)\s+(\w+)')
LITERAL = re.compile(r'^(?:unchecked\(\(u?int\)\s*)?(0[xX][0-9A-Fa-f]+|\d+)\)?$')
ARRAY = re.compile(r'\b(\w+)\s*=\s*new\s+byte\[([^\]]+)\]')
ARITH = re.compile(r'^[0-9A-Fa-fxX*+\-() ]+$')
regions = []   # [overlay, fichier, ligne, expr_base, expr_taille]
sources = {}   # (overlay, fichier) -> lignes
consts = defaultdict(dict)   # overlay -> nom -> valeur
qualified = {}               # "Classe.NOM" -> valeur, y compris depuis le SDK (voir plus bas)
for folder in FOLDERS:
    d = os.path.join(ROOT, folder)
    for name in sorted(os.listdir(d)):
        if not name.endswith(".cs"):
            continue
        src = io.open(os.path.join(d, name), encoding="utf-8-sig").read().splitlines()
        sources[(folder, name)] = src
        cls = None
        for i, line in enumerate(src, 1):
            k = CLASS.search(line)
            if k:
                cls = k.group(1)
            c = CONST.search(line)
            if c:
                lit = LITERAL.match(c.group(2).strip())
                if lit:
                    consts[folder][c.group(1)] = int(lit.group(1), 0)
                    if cls:
                        qualified[cls + "." + c.group(1)] = int(lit.group(1), 0)
            a_ = ARRAY.search(line)
            if a_:
                consts[folder].setdefault("[]" + a_.group(1), a_.group(2).strip())
            if "RamRegion(" in line and not line.lstrip().startswith("//"):
                r = REGION.search(line)
                if r:
                    regions.append([folder, name, i, r.group(1).strip(), r.group(2).strip()])
            m = DECL.match(line)
            if not m:
                continue
            # l'adresse vient de l'annotation GHIDRA la plus proche au-dessus, sinon de l'initialiseur
            addr = None
            j = i - 2
            while j >= 0 and (src[j].strip().startswith("//") or src[j].strip() == ""):
                g = GHIDRA.search(src[j])
                if g:
                    addr = g.group(1).lower()
                    break
                if src[j].strip() == "":
                    break
                j -= 1
            if addr is None:
                g = INIT.search(line)
                if g:
                    addr = g.group(1).lower()
            decls[m.group(2)].append((folder, name, i, m.group(1), "const" in line, addr))

def is_storage_dup(v):
    """Vrai si les declarations ne sont pas toutes des constantes d'adresse."""
    return not all(c for _, _, _, _, c, _ in v)

def same_address(v):
    """Vrai si toutes les declarations designent une meme adresse PSX connue."""
    addrs = {a for *_, a in v if a}
    return len(addrs) == 1 and len(addrs) == len({a for *_, a in v})

def show(title, items):
    print("=== %s : %d" % (title, len(items)))
    divergent = 0
    storage = 0
    for k, v in sorted(items):
        types = {t for _, _, _, t, _, _ in v}
        addrs = {a for *_, a in v if a}
        flag = "  <-- TYPES DIVERGENTS" if len(types) > 1 else ""
        homonym = len(addrs) > 1
        if len(types) > 1 and not homonym:
            divergent += 1
        if is_storage_dup(v) and not homonym:
            storage += 1
            flag += "  <-- STOCKAGE DUPLIQUE" if not flag else " + STOCKAGE"
        addr_note = ("" if len(addrs) <= 1 else
                     "  <-- HOMONYMES: %s, pas un doublon" % ", ".join(sorted(addrs)))
        print("  %-22s %s%s%s" % (k, " | ".join("%s/%s:%d %s%s" % (f, n, l, t, " const" if c else "")
                                                for f, n, l, t, c, _ in v), flag, addr_note))
    return len(items), divergent, storage

intra = [(k, v) for k, v in decls.items() if len({(f, n) for f, n, *_ in v if f == "VS_EXE"}) > 1]
n_intra, div_intra, sto_intra = show("VS_EXE : symbole declare dans plus d un fichier (static OU const)",
                                     [(k, [x for x in v if x[0] == "VS_EXE"]) for k, v in intra])
cross = [(k, v) for k, v in decls.items() if "VS_EXE" in {f for f, *_ in v} and len({f for f, *_ in v}) > 1]
n_cross, div_cross, sto_cross = show("VS_EXE : symbole aussi declare dans un AUTRE overlay ou a la racine", cross)

print()
print("intra-overlay: %d (dont %d divergents, %d stockages dupliques)"
      % (n_intra, div_intra, sto_intra))
print("inter-overlay: %d (dont %d divergents, %d stockages dupliques)"
      % (n_cross, div_cross, sto_cross))

# Le mode strict ne gate QUE sur l'intra-overlay et les types divergents. Les doublons
# inter-overlay sont un fait connu du portage (chaque overlay est lie separement et porte ses
# propres globales); les gater bloquerait tout commit sans rien apprendre.
# Le gate porte sur l'INTRA-overlay seulement, et c'est un choix, pas un oubli. Deux overlays sur
# une meme adresse ne sont jamais vivants ensemble (PsxSdkBridges ne tient qu'un resolveur, remplace
# a chaque LoadExec), et la RAM PSX est reutilisable: TITLE et VS rangent legitimement des choses
# differentes au meme endroit. Le seul cas connu ici, 0x1F80012C, est exactement cela et est
# documente des deux cotes. Gater dessus bloquerait chaque commit pour un fait de conception.
# Deux declarations sur une meme adresse DANS UN MEME overlay, en revanche, sont deux copies des
# memes octets qui divergent des que l'une est ecrite: c'est cela que ce mode refuse.
def arith(expr):
    """Evalue une expression PUREMENT litterale. Rien d autre n est evalue: pas d appel, pas de
    nom, pas d indexation -- un vérificateur ne doit pas executer le code qu il inspecte."""
    expr = expr.strip()
    if not ARITH.match(expr) or not any(ch.isdigit() for ch in expr):
        return None
    try:
        return eval(expr, {"__builtins__": {}}, {})   # litteraux et * + - ( ) uniquement
    except Exception:
        return None

# LE SDK EST LU POUR SES CONSTANTES, PAS POUR SES DECLARATIONS. Les tailles des structures
# primitives (POLY_FT4Ref.Size, POLY_GT4Ref.Size ...) y vivent, et sans elles une region declaree
# `RamRegion(addr, POLY_FT4Ref.Size * 5)` n'est pas mesurable. Ne pas les lire a coute cher: une
# premiere version reduisait `POLY_FT4Ref.Size` a `Size`, tombait sur `SharedHighRam.Size = 0x248`
# du portage, dimensionnait la region a 0xB68 au lieu de 0xC8 et signalait DEUX symboles comme
# contenus dans des bornes ou ils ne sont pas. Le nom qualifie est desormais exige tel quel.
SDK = os.path.join(os.path.dirname(os.path.dirname(ROOT)), "PsxSdkMonogame", "PsxSdkMonogame")
if os.path.isdir(SDK):
    for name in sorted(os.listdir(SDK)):
        if not name.endswith(".cs"):
            continue
        cls = None
        for line in io.open(os.path.join(SDK, name), encoding="utf-8-sig").read().splitlines():
            k = CLASS.search(line)
            if k:
                cls = k.group(1)
            c = CONST.search(line)
            if c and cls:
                lit = LITERAL.match(c.group(2).strip())
                if lit:
                    qualified.setdefault(cls + "." + c.group(1), int(lit.group(1), 0))


def value_of(expr, folder, depth=0):
    """Base ou taille: litteral, arithmetique litterale, `const`, longueur d un `new byte[...]`,
    ou adresse GHIDRA d un symbole DAT_. Rend None quand rien ne tranche, ce qui compte comme un
    angle mort declare plutot que comme un zero silencieux."""
    if depth > 3:
        return None
    expr = expr.strip()
    lit = LITERAL.match(expr)
    if lit:
        return int(lit.group(1), 0)
    v = arith(expr)
    if v is not None:
        return v
    if expr in qualified:
        return qualified[expr]
    if "." in expr and re.match(r'^[A-Za-z_][\w.]*$', expr):
        return None      # nom qualifie inconnu: angle mort declare, jamais une supposition
    sym = expr.split(".")[-1]

    # RamRegion(addr, someByteArray): la taille est la longueur declaree du tableau.
    for f in [folder] + [x for x in consts if x != folder]:
        if "[]" + sym in consts[f]:
            v = value_of(consts[f]["[]" + sym], f, depth + 1)
            if v is not None:
                return v

    # L'AMBIGUITE VAUT UN ANGLE MORT, PAS UNE SUPPOSITION. Le nom qualifie est reduit a son
    # dernier segment (`POLY_FT4Ref.Size` -> `Size`), et un segment aussi generique existe dans
    # plusieurs classes. Prendre la premiere trouvee a produit deux FAUX POSITIFS: la taille de
    # deux regions de cinq POLY_FT4 (0xC8) a ete resolue a 0xB68 en attrapant le `Size` d'une autre
    # classe, ce qui a fait tomber deux symboles voisins DANS des bornes ou ils ne sont pas.
    # Un vérificateur qui hurle a tort s'apprend a ignorer, donc: valeurs candidates divergentes
    # => None, et la region part en angle mort declare.
    cands = set()
    for f in [folder] + [x for x in consts if x != folder]:
        if sym in consts[f]:
            got = consts[f][sym]
            v = got if isinstance(got, int) else value_of(got, f, depth + 1)
            if v is not None:
                cands.add(v)
    if len(cands) == 1:
        return cands.pop()
    if len(cands) > 1:
        return None
    for f, _, _, _, _, a in decls.get(sym, []):
        if a and f == folder:
            return int(a, 16)
    for *_, a in decls.get(sym, []):
        if a:
            return int(a, 16)

    # Derniere chance: une expression MIXTE, du type `POLY_FT4Ref.Size * 2`. On substitue chaque
    # identifiant par sa valeur connue puis on evalue -- si et seulement si TOUS les identifiants
    # se resolvent. Un seul inconnu et on rend None, donc angle mort declare plutot que devine.
    idents = set(re.findall(r'[A-Za-z_][\w.]*', expr))
    if idents and depth < 3:
        sub = expr
        for ident in sorted(idents, key=len, reverse=True):
            v = value_of(ident, folder, depth + 1)
            if v is None:
                return None
            sub = sub.replace(ident, str(v))
        return arith(sub)
    return None

print()
print("=== VS_EXE : stockage declare DANS les bornes d une region")
contained = []
blind = []
for folder, fname, line, base_expr, size_expr in regions:
    base = value_of(base_expr, folder)
    size = value_of(size_expr, folder)
    if base is None or size is None:
        blind.append((folder, fname, line, base_expr, size_expr,
                      "base" if base is None else "taille"))
        continue
    for sym, v in decls.items():
        for f, n, l, t, is_const, a in v:
            if f != folder or a is None or is_const:
                continue          # une ADRESSE n est pas un STOCKAGE: c est la regle du portage
            if (n, l) == (fname, line):
                continue          # la region elle-meme
            addr = int(a, 16)
            if base <= addr < base + size:
                contained.append((folder, n, l, sym, t, a, fname, line, base, size))

for folder, n, l, sym, t, a, fname, line, base, size in sorted(contained):
    print("  %-22s %s/%s:%d %s @ %s  <-- DANS la region de %s:%d (0x%08X..0x%08X)"
          % (sym, folder, n, l, t, a, fname, line, base & 0xFFFFFFFF,
             (base + size) & 0xFFFFFFFF))

# UNE REGION NON RESOLUE N EST PAS UN DEFAUT, MAIS ELLE REND CETTE PASSE AVEUGLE SUR ELLE.
# Le dire est la seule chose qui donne un sens au vert: un balayage qui ne voit rien et rapporte
# "0 probleme" ment par omission. Les tailles calculees a partir de donnees (Roster) et les
# regions locales de banc en font partie.
for folder, fname, line, base_expr, size_expr, which in sorted(blind):
    print("  (angle mort) %s/%s:%d  %s non resolue  [base %s, taille %s]"
          % (folder, fname, line, which, base_expr, size_expr))

vs_blind = len([b for b in blind if b[0] == "VS_EXE"])
print("regions vues: %d, resolues: %d, angles morts: %d (dont %d dans VS_EXE)"
      % (len(regions), len(regions) - len(blind), len(blind), vs_blind))
print("stockages contenus dans une region: %d" % len(contained))

if "--strict" in sys.argv:
    fail = sto_intra + div_intra + len([c for c in contained if c[0] == "VS_EXE"])
    print("STRICT: %d probleme(s) bloquant(s) intra-overlay "
          "(stockages dupliques + types divergents + stockages contenus dans une region)" % fail)
    sys.exit(1 if fail else 0)
