#!/usr/bin/env python3
"""How much of VS.EXE is transliterated, function by function.

Reads a TSV inventory of every VS.EXE function (address, name, size, caller and
callee counts) dumped from Ghidra, and cross-references it against the port's own
`// GHIDRA: name @ 0xADDR (VS.EXE)` annotations to sort each function into one of
three buckets:

  PORTED   an annotation introduces a C# method body for that address, and that
           body is not a stub;
  STUB     an annotation introduces a method whose body is only `_ = param;` and
           an optional constant return -- the port's BLOCKED placeholder shape;
  ABSENT   no annotation in the port declares that address at all;
  EMPTY    the port's body is stub-shaped AND so is the ORIGINAL's -- the function
           in the image is `jr ra` and nothing else, so an empty C# body is the
           faithful transliteration and not a hole. Counted with PORTED. This is
           checked against `data/VS.EXE` on every run, never taken on trust from a
           list of addresses, so it cannot rot into an excuse.

Usage:  python vs_port_coverage.py <inventory.tsv> [--list BUCKET] [--top N]

Without --list it prints the three totals, by count and by byte size, plus the
largest absent functions ranked by size, which is the work queue.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PORT = ROOT / "custom-tools" / "DbzLegendsAnalyser" / "DbzLegendsRemaster" / "VS_EXE"

ANNOTATION = re.compile(
    r"//\s*GHIDRA:\s*([A-Za-z_][A-Za-z0-9_]*)\s*@\s*(0x[0-9A-Fa-f]{8})\s*\(VS\.EXE\)"
)
DECLARATION = re.compile(
    r"^\s*(?:private|internal|public|protected)\s+"
    r"(?:static\s+)?(?:readonly\s+)?"
    r"[A-Za-z_][A-Za-z0-9_<>\[\], .]*\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)\s*\("
)
WINDOW = 120

# A stub body: only discard-assignments, an optional constant return, braces and
# comments. Anything else counts as real work.
STUB_LINE = re.compile(
    r"^\s*(?:\{|\}|//.*|_\s*=\s*[A-Za-z_][A-Za-z0-9_]*\s*;|return\s+[-0-9xXa-fA-F]+\s*;|return\s*;|)$"
)


def body_is_stub(lines, decl_index):
    """decl_index is the 0-based line of the declaration. Scan its brace block."""
    depth = 0
    started = False
    for i in range(decl_index, min(decl_index + 400, len(lines))):
        line = lines[i]
        if i > decl_index and not STUB_LINE.match(line):
            return False
        depth += line.count("{") - line.count("}")
        if "{" in line:
            started = True
        if started and depth <= 0:
            return True
    return False


def scan_port():
    """Return {address: ('PORTED'|'STUB', file, line, symbol)}."""
    found = {}
    for path in sorted(PORT.glob("*.cs")):
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        for index, line in enumerate(lines):
            match = ANNOTATION.search(line)
            if not match:
                continue
            symbol, address = match.group(1), match.group(2).lower()
            for offset in range(index + 1, min(index + 1 + WINDOW, len(lines))):
                if ANNOTATION.search(lines[offset]):
                    break
                decl = DECLARATION.match(lines[offset])
                # The C# method name does NOT have to equal the annotated Ghidra symbol:
                # this port renames a function in BOTH places once the evidence is decisive
                # (FUN_80053330 -> CreateTask, FUN_80061800 -> ProcessPadInput), and the
                # annotation keeps the Ghidra spelling of the moment. So the FIRST declaration
                # after the annotation is the one it introduces, whatever it is called.
                if decl:
                    kind = "STUB" if body_is_stub(lines, offset) else "PORTED"
                    # A real body anywhere wins over a stub elsewhere.
                    if found.get(address, ("STUB",))[0] != "PORTED":
                        found[address] = (kind, path.name, offset + 1, symbol)
                    break
    return found


# THE PSX-EXE GEOMETRY. VS.EXE loads at 0x80020000 and its 0x800-byte header sits in
# front of the text, so an address maps to a file offset by subtracting the load
# address and adding the header. Confirmed against the header's own taddr/tsize words
# rather than assumed.
IMAGE = Path(__file__).resolve().parents[2] / "data" / "VS.EXE"
LOAD_ADDRESS = 0x80020000
HEADER_SIZE = 0x800


def original_is_empty(address, size):
    """True when the function in the image does nothing at all.

    MIPS `jr ra` is 0x03E00008 and `nop` is 0x00000000. A function whose whole body is
    those two words returns without touching a register, so a C# method with an empty
    body is not a stub of it -- it IS it. Anything longer, or anything with a different
    first instruction, is a real body and stays classified as a stub.
    """
    if size > 8 or not IMAGE.exists():
        return False
    offset = address - LOAD_ADDRESS + HEADER_SIZE
    blob = IMAGE.read_bytes()
    if offset < 0 or offset + size > len(blob):
        return False
    words = [
        int.from_bytes(blob[offset + i : offset + i + 4], "little")
        for i in range(0, size, 4)
    ]
    return words[:2] == [0x03E00008, 0x00000000]


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    inventory = Path(sys.argv[1])
    want_list = None
    top = 40
    args = sys.argv[2:]
    for i, arg in enumerate(args):
        if arg == "--list" and i + 1 < len(args):
            want_list = args[i + 1].upper()
        if arg == "--top" and i + 1 < len(args):
            top = int(args[i + 1])

    found = scan_port()

    rows = []
    for raw in inventory.read_text(encoding="utf-8").splitlines()[1:]:
        parts = raw.split("\t")
        if len(parts) < 5:
            continue
        address, name, size, callers, callees = parts[0].lower(), parts[1], int(parts[2]), int(parts[3]), int(parts[4])
        kind = found.get(address, ("ABSENT", "", 0, ""))[0]
        if kind == "STUB" and original_is_empty(int(address, 16), size):
            kind = "EMPTY"
        rows.append((address, name, size, callers, callees, kind))

    # THE SDK BOUNDARY. Rule 13 of the port contract says the PSX SDK is not game
    # runtime: it belongs in PsxSdkMonogame, not in a transliterated overlay. In VS.EXE
    # the linker put every library object above 0x800632C4, which is `_card_load`, the
    # first named libcard entry; everything below it is game code, `main` @ 0x80062134
    # included. Ghidra's own symbol names confirm the split -- from that address on the
    # named functions are _card_*, Spu*, _spu_*, S_SVA_OBJ_*, libgpu, libgte, libetc.
    SDK_BASE = 0x800632C4

    # AND A LOWER BOUND, WHICH IS NOT PEDANTRY. Ghidra's function manager for this
    # program holds 1362 functions, but only 1209 of them are code: the other 153 are
    # the GTE macro pseudo-functions in the separate 0x20000000 block, one byte each,
    # which the SDK header defines as inline assembly and which no overlay ever calls
    # as functions. 0x20000000 is NUMERICALLY BELOW 0x800632C4, so an upper bound
    # alone would sort every one of them into ABSENT and report 153 phantom holes in
    # a port that has none. The inventory shipped with this repository happens to
    # contain only the 0x8xxxxxxx block, so the bug never fired -- it was waiting for
    # the first person to re-dump the TSV from Ghidra.
    LOAD_BASE = 0x80000000
    rows = [r for r in rows if LOAD_BASE <= int(r[0], 16) < SDK_BASE]

    buckets = {"PORTED": [], "EMPTY": [], "STUB": [], "ABSENT": []}
    for row in rows:
        buckets[row[5]].append(row)

    if want_list:
        for address, name, size, callers, callees, _ in sorted(
            buckets[want_list], key=lambda r: -r[2]
        )[:top]:
            print(f"{address}\t{name}\t{size}\t{callers}\t{callees}")
        return 0

    total_size = sum(r[2] for r in rows)
    print(f"fonctions VS.EXE hors SDK   : {len(rows)}   ({total_size} octets)")
    print("  (le SDK PSX, >= 0x800632C4, est exclu : rule 13, il vit dans PsxSdkMonogame)")
    for kind in ("PORTED", "EMPTY", "STUB", "ABSENT"):
        group = buckets[kind]
        size = sum(r[2] for r in group)
        pct = 100.0 * size / total_size if total_size else 0.0
        print(f"  {kind:8s} : {len(group):5d} fonctions  {size:7d} octets  {pct:5.1f} %")

    closed = buckets["PORTED"] + buckets["EMPTY"]
    closed_size = sum(r[2] for r in closed)
    closed_pct = 100.0 * closed_size / total_size if total_size else 0.0
    print(f"  {'CLOS':8s} : {len(closed):5d} fonctions  {closed_size:7d} octets  {closed_pct:5.1f} %"
          "   (PORTED + EMPTY)")

    print()
    print(f"LES {top} PLUS GROSSES ABSENTES (adresse, nom, octets, appelants, appelees) :")
    for address, name, size, callers, callees, _ in sorted(
        buckets["ABSENT"], key=lambda r: -r[2]
    )[:top]:
        print(f"  {address}  {name:34s} {size:6d}  {callers:3d} <- -> {callees:3d}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
