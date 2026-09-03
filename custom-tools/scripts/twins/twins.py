"""Twin finder: for every VS.EXE function, find relocation-invariant twins in the other images and
cross with the C# port status. Writes twins.tsv (game functions not really ported in VS_EXE),
twins_sdk.tsv (same for SDK/runtime functions), doublons.tsv, all_vs.tsv (every VS function),
quasi.tsv, prefix.tsv (segmentation diagnostics).

Twin = same length and identical words under the EXTENDED masking (see psxfn.masked_words).
mots_differents for an exact twin = number of words that still differ under the refuter's STRICT
masking (0 = strictly identical). For SANS_JUMELLE rows with a quasi-twin candidate it is the
number of differing words under the extended masking (1..3).
"""
import os, sys
from collections import defaultdict
from psxfn import *
import csport

OUT = os.path.dirname(os.path.abspath(__file__))
OTHERS = ["TITLE", "SELECT", "SLPS", "MOVIE"]
SDK_START = 0x800632C4  # _card_load: first function of the SDK block in VS.EXE
SDK_NAMED = {"SpuInit", "start", "__main", "__do_global_dtors"}
SOUND_LO, SOUND_HI = 0x80067258, 0x8006C114  # FUN_-named block between libsnd/libspu entries
TINY = 12  # bytes; a function this small can be faithfully ported by a `return const;` body
PREFIX_MIN = 6  # words
EMBED_MIN = 12  # words: minimum size of a foreign function to be recognised inside a block
QUASI_MIN = 8   # words: below this, 1-3 differing words are coincidences


def load_ghidra():
    gh = {}
    for line in open(os.path.join(OUT, "ghidra_vs_funcs.tsv"), encoding="utf-8"):
        p = line.rstrip("\n").split("\t")
        if len(p) >= 3 and p[0].startswith("0x"):
            a = int(p[0], 16)
            gh[a] = (("FUN_%08x" % a) if p[1] == "." else p[1], int(p[2]))
    return gh


def port_status(ports, tag, addr, size):
    L = ports[tag].get(addr, [])
    meths = [i for i in L if i["kind"] == "METHOD"]
    if not meths:
        return "ABSENTE", None
    real = [i for i in meths if i["status"] == "REAL"]
    if real:
        return "REAL", real[0]
    if size <= TINY:
        return "REAL_TINY", meths[0]
    return "SOUCHE", meths[0]


def csname(info):
    if info is None:
        return "-", "-"
    return info["file"], "%s.%s" % (info["cls"], info["method"])


def refine(imgs, extra, rounds=6):
    """Propagate function boundaries across images: when function F of image X (ext-masked) is a
    strict prefix of function G of image Y, the code is identical up to len(F), so Y has a
    boundary at G + 4*len(F). Iterate until no new start appears."""
    log = []
    for rnd in range(rounds):
        ext = {}
        heads = defaultdict(list)
        for tag, im in imgs.items():
            for a, e in im.funcs:
                m = tuple(im.masked_words(a, e, "ext"))
                ext[(tag, a)] = m
                if len(m) >= PREFIX_MIN:
                    heads[m[:PREFIX_MIN]].append((tag, a))
        exact = defaultdict(list)
        for k, m in ext.items():
            exact[m].append(k)
        new = defaultdict(set)
        starts = {tag: {x for x, _ in im.funcs} for tag, im in imgs.items()}
        for (tag, a), m in ext.items():
            if len(m) < PREFIX_MIN:
                continue
            if any(t2 != tag for t2, _ in exact[m]):
                continue  # has an exact twin elsewhere, nothing to learn
            # (1) F is a strict prefix of G elsewhere -> boundary inside G
            for tag2, b in heads.get(m[:PREFIX_MIN], []):
                if tag2 == tag:
                    continue
                m2 = ext[(tag2, b)]
                if len(m2) <= len(m):
                    continue
                if m2[:len(m)] == m:
                    s = b + 4 * len(m)
                    if s not in starts[tag2]:
                        new[tag2].add(s)
                        log.append((rnd, "prefix", tag, a, tag2, b, s))
            # (2) a whole function G of another image is embedded in F at an internal `jr ra`
            #     boundary -> boundaries inside F at k and k+len(G) (G >= EMBED_MIN words)
            im = imgs[tag]
            i0 = im.idx(a)
            for k in range(2, len(m) - EMBED_MIN + 1):
                if im.words[i0 + k - 2] != JR_RA:
                    continue
                for tag2, b in heads.get(m[k:k + PREFIX_MIN], []):
                    if tag2 == tag:
                        continue
                    m2 = ext[(tag2, b)]
                    if len(m2) < EMBED_MIN or k + len(m2) > len(m):
                        continue
                    if m[k:k + len(m2)] == m2:
                        s = a + 4 * k
                        if s not in starts[tag]:
                            new[tag].add(s)
                            log.append((rnd, "embed", tag2, b, tag, a, s))
                        s2 = a + 4 * (k + len(m2))
                        if k + len(m2) < len(m) and s2 not in starts[tag]:
                            new[tag].add(s2)
                            log.append((rnd, "embed-end", tag2, b, tag, a, s2))
                        break
        if not new:
            break
        for tag2, ss in new.items():
            cur = extra.setdefault(tag2, set())
            cur |= ss
            imgs[tag2].segment(cur)
            imgs[tag2].funcs_map = {a: e for a, e in imgs[tag2].funcs}
    return log


def main():
    extra = {k: set(v) for k, v in load_extra().items()}
    imgs = load_all(extra)
    ports = csport.load_all()
    gh = load_ghidra()
    vs = imgs["VS"]
    for tag, im in imgs.items():
        im.funcs_map = {a: e for a, e in im.funcs}
    log = refine(imgs, extra)
    print("propagation de frontieres: %d nouveaux departs" % len(log))
    for tag in imgs:
        print("  %-6s funcs=%d" % (tag, len(imgs[tag].funcs)))
    with open(os.path.join(OUT, "propagation.tsv"), "w", encoding="utf-8") as f:
        f.write("round\ttype\tsource_img\tsource_fn\tcible_img\tcible_fn\tnouveau_depart\n")
        for r in log:
            f.write("%d\t%s\t%s\t0x%08X\t%s\t0x%08X\t0x%08X\n" % r)

    strict, ext = {}, {}
    for tag, im in imgs.items():
        for a, e in im.funcs:
            strict[(tag, a)] = tuple(im.masked_words(a, e, "strict"))
            ext[(tag, a)] = tuple(im.masked_words(a, e, "ext"))
    index = defaultdict(list)       # ext tuple -> [(tag, a)]
    index_strict = defaultdict(list)
    bylen = defaultdict(list)
    head8 = defaultdict(list)       # first PREFIX_MIN ext words -> [(tag,a)]
    for tag in OTHERS:
        for a, e in imgs[tag].funcs:
            m = ext[(tag, a)]
            index[m].append((tag, a))
            index_strict[strict[(tag, a)]].append((tag, a))
            bylen[(tag, len(m))].append(a)
            head8[m[:PREFIX_MIN]].append((tag, a))
    fn_start = {tag: {a for a, e in im.funcs} for tag, im in imgs.items()}

    def callee_consistency(tag, a_vs, a_ot):
        cv = vs.calls(a_vs, vs.funcs_map[a_vs])
        co = imgs[tag].calls(a_ot, imgs[tag].funcs_map[a_ot])
        n = ok = unk = 0
        for (i1, t1), (i2, t2) in zip(cv, co):
            n += 1
            if t1 in fn_start["VS"] and t2 in fn_start[tag]:
                if ext[("VS", t1)] == ext[(tag, t2)]:
                    ok += 1
            else:
                unk += 1
        return ok, unk, n

    pref = {t: k for k, t in enumerate(OTHERS)}
    rows, all_rows, doublons, quasi, prefix = [], [], [], [], []
    cats = {}

    # layout deltas (b - a) of VS functions with a UNIQUE exact twin per image, to disambiguate
    # VS functions that have several exact candidates in one image (functions identical modulo a
    # relocated constant, e.g. port-0 / port-1 variants)
    uniq = defaultdict(list)  # tag -> sorted [(a, b - a)]
    for a, e in vs.funcs:
        cands = index.get(ext[("VS", a)], [])
        per = defaultdict(list)
        for tag, b in cands:
            per[tag].append(b)
        for tag, bs in per.items():
            if len(bs) == 1:
                uniq[tag].append((a, bs[0] - a))
    for tag in uniq:
        uniq[tag].sort()

    def nearest_delta(tag, a):
        L = uniq.get(tag)
        if not L:
            return None
        import bisect
        k = bisect.bisect_left(L, (a, -1 << 40))
        best = None
        for j in (k - 1, k):
            if 0 <= j < len(L):
                if best is None or abs(L[j][0] - a) < abs(L[best][0] - a):
                    best = j
        return L[best][1]
    ann_not_start = [a for a in ports["VS"]
                     if any(i["kind"] == "METHOD" for i in ports["VS"][a]) and vs.in_code(a) and a not in fn_start["VS"]]

    for a, e in vs.funcs:
        size = e - a
        name, gsize = gh.get(a, (None, None))
        in_ghidra = name is not None
        if not in_ghidra:
            L = ports["VS"].get(a, [])
            name = (L[0]["ann_name"] if L else "FUN_%08x" % a) + "*"
        is_sdk = a >= SDK_START or name in SDK_NAMED
        if SOUND_LO <= a < SOUND_HI and name.startswith("FUN_"):
            cat = "SON"
        elif is_sdk:
            cat = "SDK"
        else:
            cat = "JEU"
        cats[a] = cat
        st, info = port_status(ports, "VS", a, size)
        m = ext[("VS", a)]
        ms = strict[("VS", a)]
        exact = index.get(m, [])
        n_strict = len(index_strict.get(ms, []))
        cand = []
        for tag, b in exact:
            st2, info2 = port_status(ports, tag, b, size)
            dstrict = sum(1 for x, y in zip(ms, strict[(tag, b)]) if x != y)
            cand.append((tag, b, st2, info2, dstrict))
        def cand_key(c):
            d = nearest_delta(c[0], a)
            mis = abs((c[1] - a) - d) if d is not None else 0
            ok, unk, ncall = callee_consistency(c[0], a, c[1])
            bad = ncall - ok - unk
            return (0 if c[2].startswith("REAL") else 1, bad, pref[c[0]], mis, c[1])
        cand.sort(key=cand_key)
        best_quasi = None
        pfx = None
        if not exact and len(m) >= QUASI_MIN:
            for tag in OTHERS:
                for b in bylen[(tag, len(m))]:
                    m2 = ext[(tag, b)]
                    d = sum(1 for x, y in zip(m, m2) if x != y)
                    if 1 <= d <= 3 and (best_quasi is None or d < best_quasi[2]):
                        best_quasi = (tag, b, d)
            # prefix diagnostics: VS function is a prefix of another, or another is a prefix of it
            if not exact and len(m) >= PREFIX_MIN:
                for tag, b in head8.get(m[:PREFIX_MIN], []):
                    m2 = ext[(tag, b)]
                    k = min(len(m), len(m2))
                    common = 0
                    for x, y in zip(m, m2):
                        if x != y:
                            break
                        common += 1
                    if common >= PREFIX_MIN and (common == len(m) or common == len(m2)):
                        rel = "VS_prefixe_de_autre" if common == len(m) else "autre_prefixe_de_VS"
                        if pfx is None or common > pfx[3]:
                            pfx = (tag, b, len(m2) * 4, common, rel)
        if cand:
            tag, b, st2, info2, dstrict = cand[0]
            issue = "PORTEE_AILLEURS" if st2.startswith("REAL") else "JUMELLE_NON_PORTEE"
            f, n = csname(info2)
            ok, unk, ncall = callee_consistency(tag, a, b)
            twin = (tag, "0x%08X" % b, f, n, dstrict, "%d/%d(+%d?)" % (ok, ncall, unk), len(exact), n_strict)
            alt = ";".join("%s:0x%08X:%s" % (c[0], c[1], c[2]) for c in cand[1:])
        elif best_quasi:
            tag, b, d = best_quasi
            st2, info2 = port_status(ports, tag, b, size)
            issue = "SANS_JUMELLE"
            f, n = csname(info2) if st2.startswith("REAL") else ("-", "-")
            twin = (tag, "0x%08X" % b, f, n, d, "-", 0, 0)
            quasi.append((a, name, size, st, tag, b, d, st2, f, n))
            alt = ""
        else:
            issue = "SANS_JUMELLE"
            twin = ("-", "-", "-", "-", "-", "-", 0, 0)
            alt = ""
        if pfx:
            prefix.append((a, name, size, st, is_sdk) + pfx)
        all_rows.append((a, name, size, st, is_sdk, issue) + twin + (gsize, alt))
        if st.startswith("REAL"):
            for tag, b, st2, info2, dstrict in cand:
                if st2.startswith("REAL"):
                    f, n = csname(info2)
                    fv, nv = csname(info)
                    doublons.append((a, name, size, fv, nv, tag, b, f, n, dstrict, is_sdk))
            continue
        etat = "SOUCHE" if st == "SOUCHE" else "ABSENTE"
        rows.append((a, name, size, etat, issue, is_sdk) + twin)

    hdr = "adresse_vs\tnom_ghidra_vs\ttaille\tetat_vs\tissue\timage_jumelle\tadresse_jumelle\tfichier_cs\tnom_csharp\tmots_differents\n"
    with open(os.path.join(OUT, "twins.tsv"), "w", encoding="utf-8") as f, \
            open(os.path.join(OUT, "twins_jeu.tsv"), "w", encoding="utf-8") as g:
        f.write(hdr)
        g.write(hdr)
        for r in rows:
            a, name, size, etat, issue, is_sdk, tag, b, fcs, ncs, d, cc, nex, nst = r
            line = "0x%08X\t%s\t%d\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n" % (a, name, size, etat, issue, tag, b, fcs, ncs, d)
            f.write(line)
            if cats[a] == "JEU":
                g.write(line)
    with open(os.path.join(OUT, "all_vs.tsv"), "w", encoding="utf-8") as f:
        f.write("adresse\tnom\ttaille\tetat_vs\tsdk\tissue\timg\tadr\tfichier\tcs\tdiff_strict\tcallees_ok\tn_exact_ext\tn_exact_strict\ttaille_ghidra\tautres_candidats\tcat\n")
        for r in all_rows:
            f.write("\t".join("0x%08X" % x if k == 0 else str(x) for k, x in enumerate(r)) + "\t" + cats[r[0]] + "\n")
    with open(os.path.join(OUT, "doublons.tsv"), "w", encoding="utf-8") as f:
        f.write("adresse_vs\tnom\ttaille\tfichier_vs\tcs_vs\timage\tadresse\tfichier\tcs\tdiff_strict\tsdk\n")
        for d in doublons:
            f.write("0x%08X\t%s\t%d\t%s\t%s\t%s\t0x%08X\t%s\t%s\t%d\t%s\n" % d)
    with open(os.path.join(OUT, "quasi.tsv"), "w", encoding="utf-8") as f:
        f.write("adresse_vs\tnom\ttaille\tetat_vs\timage\tadresse\tmots_differents\tetat_jumelle\tfichier\tcs\n")
        for q in quasi:
            f.write("0x%08X\t%s\t%d\t%s\t%s\t0x%08X\t%d\t%s\t%s\t%s\n" % q)
    with open(os.path.join(OUT, "prefix.tsv"), "w", encoding="utf-8") as f:
        f.write("adresse_vs\tnom\ttaille\tetat_vs\tsdk\timage\tadresse\ttaille_autre\tmots_communs\trelation\n")
        for p in prefix:
            f.write("0x%08X\t%s\t%d\t%s\t%s\t%s\t0x%08X\t%d\t%d\t%s\n" % p)

    print("VS funcs: %d (%s)" % (len(all_rows), ", ".join("%s %d" % (c, sum(1 for r in all_rows if cats[r[0]] == c)) for c in ("JEU", "SON", "SDK"))))
    for scope in ("JEU", "SON", "SDK"):
        sel = [r for r in all_rows if cats[r[0]] == scope]
        print("== %s ==" % scope)
        byst = defaultdict(lambda: [0, 0])
        for r in sel:
            byst[r[3]][0] += 1
            byst[r[3]][1] += r[2]
        print("  etat VS:", dict(byst))
        byiss = defaultdict(lambda: [0, 0])
        for r in sel:
            if r[3].startswith("REAL"):
                continue
            byiss[r[5]][0] += 1
            byiss[r[5]][1] += r[2]
        print("  issues (non portees):", dict(byiss))
        tw = sum(1 for r in sel if r[12] > 0)
        tws = sum(1 for r in sel if r[13] > 0)
        print("  avec jumelle exacte: ext %d / strict %d / %d fonctions" % (tw, tws, len(sel)))
    print("doublons (VS REAL + jumelle REAL ailleurs): %d" % len(doublons))
    print("quasi-jumelles (1-3 mots, meme taille, sans jumelle exacte): %d" % len(quasi))
    print("diagnostics prefixe: %d" % len(prefix))
    print("annotations VS METHOD hors debut de fonction: %s" % ", ".join("0x%08X" % a for a in sorted(ann_not_start)))
    mism = [(r[0], r[2], r[14]) for r in all_rows if r[14] not in (None, 1) and r[14] != r[2] and not r[4]]
    print("taille != Ghidra (zone jeu): %d" % len(mism))
    for a, s, g in mism:
        print("   0x%08X mine=%d ghidra=%d" % (a, s, g))


if __name__ == "__main__":
    main()
