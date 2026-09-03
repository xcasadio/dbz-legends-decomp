"""PSX-EXE function segmentation + relocation-invariant hashing.

Usage: python psxfn.py  -> writes <scratch>/twins/funcs_<IMG>.tsv and prints stats.
Read-only on the repository.
"""
import struct, sys, os, json
from collections import defaultdict

REPO = "D:/development/repo/dbz-legends-decomp"
OUT = os.path.dirname(os.path.abspath(__file__))
IMAGES = {
    "VS": "data/VS.EXE",
    "TITLE": "data/TITLE.EXE",
    "SELECT": "data/SELECT.EXE",
    "MOVIE": "data/MOVIE.EXE",
    "SLPS": "data/SLPS_003.55",
}

# ---------------------------------------------------------------- decode helpers
OP = lambda w: (w >> 26) & 0x3F
RS = lambda w: (w >> 21) & 0x1F
RT = lambda w: (w >> 16) & 0x1F
RD = lambda w: (w >> 11) & 0x1F
SA = lambda w: (w >> 6) & 0x1F
FUNCT = lambda w: w & 0x3F
IMM = lambda w: w & 0xFFFF
SIMM = lambda w: struct.unpack("<h", struct.pack("<H", w & 0xFFFF))[0]

JR_RA = 0x03E00008
NOP = 0

# I-type opcodes whose 16-bit immediate is a relocatable low half when rs came from lui
RELOC_ITYPE = {
    0x09,  # addiu
    0x0D,  # ori
    0x23, 0x2B,  # lw sw
    0x21, 0x29,  # lh sh
    0x20, 0x28,  # lb sb
    0x25, 0x24,  # lhu lbu
    0x22, 0x26, 0x2A, 0x2E,  # lwl lwr swl swr
    0x32, 0x3A,  # lwc2 swc2 (GTE)
}

LOADSTORE = {0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x28, 0x29, 0x2A, 0x2B, 0x2E, 0x32, 0x3A}
VALID_OPS = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0xA, 0xB, 0xC, 0xD, 0xE, 0xF, 0x10, 0x12} | LOADSTORE
SHIFT_IMM = {0, 2, 3}
SHIFT_REG = {4, 6, 7}
ALU3 = {0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x2A, 0x2B}
MULDIV = {0x18, 0x19, 0x1A, 0x1B}


def plausible(w):
    """Strict-ish MIPS I plausibility (rejects most data words)."""
    op = OP(w)
    if op not in VALID_OPS:
        return False
    if op == 0:
        f = FUNCT(w)
        if f in SHIFT_IMM:
            return RS(w) == 0
        if f in SHIFT_REG or f in ALU3:
            return SA(w) == 0
        if f == 8:  # jr
            return RT(w) == 0 and RD(w) == 0 and SA(w) == 0
        if f == 9:  # jalr
            return RT(w) == 0 and SA(w) == 0
        if f in (0xC, 0xD):  # syscall/break
            return True
        if f in (0x10, 0x12):  # mfhi/mflo
            return RS(w) == 0 and RT(w) == 0 and SA(w) == 0
        if f in (0x11, 0x13):  # mthi/mtlo
            return RT(w) == 0 and RD(w) == 0 and SA(w) == 0
        if f in MULDIV:
            return RD(w) == 0 and SA(w) == 0
        return False
    if op == 1:
        return RT(w) in (0, 1, 0x10, 0x11)
    if op == 0x10:  # cop0
        return RS(w) in (0, 4, 0x10)
    if op == 0x12:  # cop2
        return True
    if op in LOADSTORE:
        return RS(w) != 0  # base $zero is essentially never emitted
    if op == 0x0F:  # lui
        return RS(w) == 0
    return True


def is_prologue(w):
    return OP(w) == 9 and RS(w) == 29 and RT(w) == 29 and SIMM(w) < 0


# ---------------------------------------------------------------- image
class Image:
    def __init__(self, tag, path):
        self.tag = tag
        with open(path, "rb") as f:
            data = f.read()
        self.t_addr = struct.unpack_from("<I", data, 0x18)[0]
        self.t_size = struct.unpack_from("<I", data, 0x1C)[0]
        self.pc = struct.unpack_from("<I", data, 0x10)[0]
        body = data[0x800:0x800 + self.t_size]
        self.words = list(struct.unpack("<%dI" % (len(body) // 4), body[: len(body) // 4 * 4]))
        self.n = len(self.words)
        self.regions = []  # list of (i0, i1) word indices
        self.funcs = []  # list of (start, end) addresses

    def addr(self, i):
        return self.t_addr + 4 * i

    def idx(self, a):
        return (a - self.t_addr) // 4

    def in_image(self, a):
        return self.t_addr <= a < self.t_addr + 4 * self.n and (a & 3) == 0

    def in_code(self, a):
        if not self.in_image(a):
            return False
        i = self.idx(a)
        return any(r0 <= i < r1 for r0, r1 in self.regions)

    # -------------------------------------------------- code region detection
    def detect_regions(self, gap_words=0x1000, lookback=8):
        """Anchors = `jr ra` whose 8 preceding + 1 following words are plausible.
        Cluster anchors closer than gap_words; region = [walk-back start .. last anchor + 2]."""
        anchors = []
        for i in range(lookback, self.n - 1):
            if self.words[i] == JR_RA and all(plausible(self.words[j]) for j in range(i - lookback, i + 2)):
                anchors.append(i)
        clusters = []
        for a in anchors:
            if clusters and a - clusters[-1][-1] <= gap_words:
                clusters[-1].append(a)
            else:
                clusters.append([a])
        regions = []
        for c in clusters:
            if len(c) < 3:  # a stray anchor in data
                continue
            first, last = c[0], c[-1]
            j = first
            while j > 0 and plausible(self.words[j - 1]):
                j -= 1
            regions.append((j, last + 2))
        self.anchors = anchors
        self.regions = regions
        return regions

    # -------------------------------------------------- function starts
    def segment(self, extra_starts=()):
        starts = set()
        src = defaultdict(set)
        # 1. jal targets from code regions
        for r0, r1 in self.regions:
            for i in range(r0, r1):
                w = self.words[i]
                if OP(w) == 3:
                    tgt = ((self.addr(i) + 4) & 0xF0000000) | ((w & 0x03FFFFFF) << 2)
                    if self.in_code(tgt):
                        starts.add(tgt)
                        src[tgt].add("jal")
        # 2. prologue preceded by jr ra + delay slot; gcc may hoist up to a few loads before the
        #    `addiu sp,sp,-N`, so also accept jr ra at i-3..i-6 when nothing in between branches
        for r0, r1 in self.regions:
            for i in range(max(r0, 2), r1):
                if not is_prologue(self.words[i]):
                    continue
                if self.words[i - 2] == JR_RA:
                    starts.add(self.addr(i))
                    src[self.addr(i)].add("pro")
                    continue
                for back in range(3, 7):
                    j = i - back
                    if j < r0:
                        break
                    if self.words[j] == JR_RA:
                        between = self.words[j + 2:i]
                        if all(OP(w) not in (1, 2, 3, 4, 5, 6, 7) and not (OP(w) == 0 and FUNCT(w) in (8, 9)) for w in between):
                            starts.add(self.addr(j + 2))
                            src[self.addr(j + 2)].add("pro+")
                        break
        # 3. pointers anywhere in the image to a prologue in code, or to a word that directly
        #    follows a `jr ra` + delay slot (a frameless leaf reached only through a table)
        for i in range(self.n):
            v = self.words[i]
            if self.in_code(v):
                j = self.idx(v)
                if is_prologue(self.words[j]):
                    starts.add(v)
                    src[v].add("ptr")
                elif j >= 2 and self.words[j - 2] == JR_RA and self.in_code(self.addr(j - 2)):
                    starts.add(v)
                    src[v].add("ptr2")
        # 3b. addresses materialised in code by lui + addiu/ori within 8 instructions, pointing
        #     into code at a prologue or right after a `jr ra` + delay slot (task callbacks,
        #     handlers handed to CreateTask, etc.)
        for r0, r1 in self.regions:
            lui_at = {}
            for i in range(r0, r1):
                w = self.words[i]
                op = OP(w)
                if op == 0x0F:
                    lui_at[RT(w)] = (i, IMM(w))
                    continue
                if op in (9, 0x0D):
                    r = RS(w)
                    if r in lui_at and i - lui_at[r][0] <= 8:
                        hi = lui_at[r][1]
                        v = (hi << 16) + (SIMM(w) if op == 9 else IMM(w))
                        v &= 0xFFFFFFFF
                        if self.in_code(v):
                            j = self.idx(v)
                            if is_prologue(self.words[j]) or (j >= 2 and self.words[j - 2] == JR_RA and self.in_code(self.addr(j - 2))):
                                starts.add(v)
                                src[v].add("lui")
        # 4. extra (e.g. Ghidra) starts
        for a in extra_starts:
            if self.in_code(a):
                starts.add(a)
                src[a].add("ext")
        # entry point and region starts
        if self.in_code(self.pc):
            starts.add(self.pc)
            src[self.pc].add("pc")
        for r0, r1 in self.regions:
            a = self.addr(r0)
            # region start is a function start only if nothing else claims the first words;
            # we add it so that no code is orphaned (it may be a real function w/o prologue)
            starts.add(a)
            src[a].add("reg")
        ss = sorted(starts)
        funcs = []
        for k, a in enumerate(ss):
            i = self.idx(a)
            r1 = next(r1 for r0, r1 in self.regions if r0 <= i < r1)
            e = ss[k + 1] if k + 1 < len(ss) and self.idx(ss[k + 1]) <= r1 else self.addr(r1)
            funcs.append((a, min(e, self.addr(r1))))
        self.funcs = funcs
        self.start_src = src
        return funcs

    # -------------------------------------------------- hashing
    def masked_words(self, start, end, mode="strict"):
        """Return list of masked 32-bit words for [start,end).

        mode="strict": the refuter's rule — mask jal/j targets, lui immediates, and the 16-bit
        immediate of addiu/ori/loads/stores whose rs was written by a lui within the previous
        8 instructions.
        mode="ext": same, but (a) a register stays "lui-tainted" until it is overwritten by any
        other instruction (linear scan, no window), and (b) gp-relative immediates (rs == $gp) are
        masked too — R_MIPS_GPREL16 relocations into .sdata/.sbss.
        """
        i0, i1 = self.idx(start), self.idx(end)
        out = []
        last_lui = {}  # reg -> index of last lui (strict window rule, never cleared)
        taint = {}     # reg -> index of the lui its value derives from (ext rule)
        for i in range(i0, i1):
            w = self.words[i]
            op = OP(w)
            m = w
            dest = None
            if op in (2, 3):
                m = w & 0xFC000000
            elif op == 0x0F:
                m = w & 0xFFFF0000
                last_lui[RT(w)] = i
                taint[RT(w)] = i
                out.append(m)
                continue
            elif op in RELOC_ITYPE:
                r = RS(w)
                if r in last_lui and i - last_lui[r] <= 8:
                    m = w & 0xFFFF0000
                elif mode == "ext" and r in taint:
                    m = w & 0xFFFF0000
                elif mode == "ext" and r == 28 and op != 0x0D:
                    m = w & 0xFFFF0000
            if mode == "ext":
                # taint propagation: add/addu of a tainted register keeps the address tainted
                # (lui base ; addu base,base,index ; lw x,lo(base)); anything else that writes a
                # register clears its taint
                if op == 0:
                    f = FUNCT(w)
                    if f in (0x20, 0x21) and (RS(w) in taint or RT(w) in taint) and RD(w) != 0:
                        taint[RD(w)] = taint.get(RS(w), taint.get(RT(w)))
                        out.append(m)
                        continue
                    if f not in (8, 0xC, 0xD, 0x11, 0x13, 0x18, 0x19, 0x1A, 0x1B):
                        dest = RD(w)
                elif op in (8, 9, 0xA, 0xB, 0xC, 0xD, 0xE, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26):
                    dest = RT(w)
                elif op == 3:
                    dest = 31
                elif op == 0x12 and RS(w) in (0, 2):  # mfc2/cfc2
                    dest = RT(w)
                if dest is not None and dest in taint and dest != 0:
                    del taint[dest]
            out.append(m)
        return out

    def raw_words(self, start, end):
        return self.words[self.idx(start):self.idx(end)]

    def calls(self, start, end):
        """List of (word index within function, target) for jal."""
        res = []
        i0, i1 = self.idx(start), self.idx(end)
        for i in range(i0, i1):
            w = self.words[i]
            if OP(w) == 3:
                res.append((i - i0, ((self.addr(i) + 4) & 0xF0000000) | ((w & 0x03FFFFFF) << 2)))
        return res


def load_all(extra=None):
    imgs = {}
    for tag, rel in IMAGES.items():
        im = Image(tag, os.path.join(REPO, rel))
        im.detect_regions()
        im.segment(extra.get(tag, ()) if extra else ())
        imgs[tag] = im
    return imgs


def load_extra():
    extra = {}
    gh = os.path.join(OUT, "ghidra_vs_funcs.tsv")
    if os.environ.get("USE_GHIDRA_STARTS") and os.path.exists(gh):
        ex = []
        limit = int(os.environ.get("GHIDRA_STARTS_BELOW", "0x800632C4"), 16)
        for line in open(gh, encoding="utf-8"):
            p = line.rstrip("\n").split("\t")
            if len(p) >= 1 and p[0].startswith("0x") and int(p[0], 16) < limit:
                ex.append(int(p[0], 16))
        extra["VS"] = ex
    return extra


if __name__ == "__main__":
    imgs = load_all(load_extra())
    for tag, im in imgs.items():
        regs = " ".join("[%08X-%08X]" % (im.addr(a), im.addr(b)) for a, b in im.regions)
        print("%-6s t_addr=%08X t_size=%06X pc=%08X funcs=%d regions=%s" % (
            tag, im.t_addr, im.t_size, im.pc, len(im.funcs), regs))
        with open(os.path.join(OUT, "funcs_%s.tsv" % tag), "w", encoding="utf-8") as f:
            for a, e in im.funcs:
                f.write("0x%08X\t0x%08X\t%d\t%s\n" % (a, e, e - a, ",".join(sorted(im.start_src[a]))))
