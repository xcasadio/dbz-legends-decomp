#!/usr/bin/env python3
"""One Ghidra function address must be DECLARED in exactly one C# file.

WHY THIS EXISTS. The recurring defect in this port is not a compile error: the same
Ghidra address gets a real body in one file and an empty stub in another, C# binds an
unqualified call to the enclosing class first, and the stub silently wins. It compiles,
it runs, and the work is simply not done. The repository has shipped that four times.

The existing check_duplicate_symbols.py covers DATA (a global declared twice). This
covers CODE, and it has to tell two things apart that look identical to a text search:

  * a DECLARATION -- a `// GHIDRA: name @ 0xADDR (EXE)` annotation whose next few lines
    actually define a method with that name;
  * a CROSS-REFERENCE -- the same annotation used inside a comment to point at a
    function that lives somewhere else, which is exactly what this port is supposed to
    do instead of redeclaring.

Only two DECLARATIONS of one address are a defect. Everything else is reported as
context so the count is auditable rather than a bare "OK".

Run from anywhere; paths are resolved from this file's own location.
"""

import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PORT = ROOT / "custom-tools" / "DbzLegendsAnalyser" / "DbzLegendsRemaster"

ANNOTATION = re.compile(
    r"//\s*GHIDRA:\s*([A-Za-z_][A-Za-z0-9_]*)\s*@\s*(0x[0-9A-Fa-f]{8})\s*\(([A-Za-z0-9_.]+)\)"
)

# A method declaration, C# style, on one line: modifiers, return type, name, "(".
DECLARATION = re.compile(
    r"^\s*(?:private|internal|public|protected)\s+"
    r"(?:static\s+)?(?:readonly\s+)?"
    r"[A-Za-z_][A-Za-z0-9_<>\[\], .]*\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)\s*\("
)

# How many lines after the annotation a declaration may appear. The annotation block
# carries the analysis prose, which in this repository is routinely long.
WINDOW = 120


def classify(lines, index, symbol):
    """Return the 1-based line of the declaration this annotation introduces, or None.

    The annotation introduces a declaration when a method whose NAME matches the
    annotated symbol appears before the next `// GHIDRA:` annotation.
    """
    for offset in range(index + 1, min(index + 1 + WINDOW, len(lines))):
        line = lines[offset]
        if ANNOTATION.search(line):
            return None
        match = DECLARATION.match(line)
        if match and match.group(1) == symbol:
            return offset + 1
    return None


def main():
    declarations = defaultdict(list)
    references = defaultdict(list)

    for path in sorted(PORT.glob("*/*.cs")):
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        for index, line in enumerate(lines):
            match = ANNOTATION.search(line)
            if not match:
                continue
            symbol, address, exe = match.group(1), match.group(2).lower(), match.group(3)
            key = (exe, address)
            declared_at = classify(lines, index, symbol)
            rel = path.relative_to(PORT).as_posix()
            if declared_at is None:
                references[key].append((rel, index + 1, symbol))
            else:
                declarations[key].append((rel, declared_at, symbol))

    clashes = {k: v for k, v in declarations.items() if len(v) > 1}

    print(f"annotations declarant un corps : {sum(len(v) for v in declarations.values())}")
    print(f"annotations en renvoi (commentaire seul) : {sum(len(v) for v in references.values())}")
    print(f"adresses distinctes declarees : {len(declarations)}")

    if not clashes:
        print()
        print("invariant tenu : aucune adresse Ghidra n'est declaree dans deux fichiers")
        return 0

    print()
    for (exe, address), sites in sorted(clashes.items()):
        print(f"  {exe} {address}  <-- DECLAREE {len(sites)} FOIS")
        for rel, line, symbol in sites:
            print(f"      {rel}:{line}  {symbol}")
    print()
    print(f"invariant ROMPU : {len(clashes)} adresse(s) declaree(s) plus d'une fois")
    return 1


if __name__ == "__main__":
    sys.exit(main())
