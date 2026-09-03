"""Manual verification helper: side-by-side disassembly of VS function vs its twin.
Prints the first N instructions of both, and every word that differs under the STRICT masking
(with both disassemblies), so the reader can see they are relocations.
Usage: python verify.py [N] -> the 3 largest PORTEE_AILLEURS (JEU) + 5 random others (seed 42)
"""
import os, sys, random
from psxfn import *
from disasm import dis
from twins import OUT, load_ghidra

N = int(sys.argv[1]) if len(sys.argv) > 1 else 10


def main():
    imgs = load_all(load_extra())
    # re-read chosen twins from all_vs.tsv (already refined segmentation is not needed for the
    # side-by-side: we only need start addresses and sizes)
    rows = []
    for line in open(os.path.join(OUT, "all_vs.tsv"), encoding="utf-8"):
        p = line.rstrip("\n").split("\t")
        if p[0] == "adresse":
            continue
        rows.append(p)
    pa = [p for p in rows if p[5] == "PORTEE_AILLEURS" and p[16] == "JEU" and not p[3].startswith("REAL")]
    pa.sort(key=lambda p: -int(p[2]))
    big3 = pa[:3]
    rest = pa[3:]
    random.seed(42)
    rnd5 = random.sample(rest, 5)
    for label, sel in (("3 PLUS GROSSES", big3), ("5 AU HASARD (seed 42)", rnd5)):
        print("=" * 100)
        print(label)
        for p in sel:
            a, name, size, tag, b = int(p[0], 16), p[1], int(p[2]), p[6], int(p[7], 16)
            e, e2 = a + size, b + size
            vs, ot = imgs["VS"], imgs[tag]
            print("-" * 100)
            print("VS %s @ 0x%08X (%d octets)  <->  %s @ 0x%08X  port: %s %s  callees_ok=%s  diff_strict=%s" % (
                name, a, size, tag, b, p[8], p[9], p[11], p[10]))
            wv, wo = vs.raw_words(a, e), ot.raw_words(b, e2)
            ms_v, ms_o = vs.masked_words(a, e, "strict"), ot.masked_words(b, e2, "strict")
            me_v, me_o = vs.masked_words(a, e, "ext"), ot.masked_words(b, e2, "ext")
            assert me_v == me_o, "not identical under ext masking!"
            print("  %-10s %-8s  %-34s | %-10s %-8s  %s" % ("VS addr", "word", "disasm", tag + " addr", "word", "disasm"))
            for i in range(min(N, len(wv))):
                print("  0x%08X %08X  %-34s | 0x%08X %08X  %s%s" % (
                    a + 4 * i, wv[i], dis(wv[i], a + 4 * i), b + 4 * i, wo[i], dis(wo[i], b + 4 * i),
                    "" if wv[i] == wo[i] else "   <- differe (brut)"))
            nd_raw = sum(1 for x, y in zip(wv, wo) if x != y)
            nd_strict = sum(1 for x, y in zip(ms_v, ms_o) if x != y)
            print("  mots bruts differents: %d / %d ; apres masquage strict: %d ; apres masquage etendu: 0" % (nd_raw, len(wv), nd_strict))
            if nd_strict:
                print("  mots encore differents sous le masquage STRICT (tous des relocations gp / lui lointain):")
                shown = 0
                for i, (x, y) in enumerate(zip(ms_v, ms_o)):
                    if x != y:
                        print("    0x%08X %08X  %-34s | 0x%08X %08X  %s" % (
                            a + 4 * i, wv[i], dis(wv[i], a + 4 * i), b + 4 * i, wo[i], dis(wo[i], b + 4 * i)))
                        shown += 1
                        if shown >= 6:
                            print("    ... (%d au total)" % nd_strict)
                            break


if __name__ == "__main__":
    main()
