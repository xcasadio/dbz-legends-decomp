"""Parse `// GHIDRA: name @ 0xADDR` annotations in the C# port and classify the method
that follows each one as REAL (has instructions) or STUB (empty / `_ = p;` / `return const;`
/ `throw`). Read-only.

Output per overlay: dict addr -> list of dict(file, cls, method, kind, status, nstmt, line)
kind: METHOD | FIELD | OTHER
"""
import re, os, glob, sys

REPO = "D:/development/repo/dbz-legends-decomp"
ROOT = REPO + "/custom-tools/DbzLegendsAnalyser/DbzLegendsRemaster"
OVERLAYS = {"VS": "VS_EXE", "TITLE": "TITLE_EXE", "SELECT": "SELECT_EXE", "MOVIE": "MOVIE_EXE", "SLPS": "SLPS_003_55"}
SUFFIX = {"VS": "VS.EXE", "TITLE": "TITLE.EXE", "SELECT": "SELECT.EXE", "MOVIE": "MOVIE.EXE", "SLPS": "SLPS_003.55"}

ANN = re.compile(r'^\s*//\s*GHIDRA:\s*(.*)$')
PAIR = re.compile(r'([A-Za-z_][A-Za-z0-9_]*)\s*@\s*0x([0-9A-Fa-f]{6,8})')
CLASS = re.compile(r'\b(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)')
STUB_STMT = [
    re.compile(r'^_\s*=\s*[A-Za-z_][A-Za-z0-9_]*$'),
    re.compile(r'^return$'),
    re.compile(r'^return\s+(?:-?\d+|0x[0-9A-Fa-f]+|true|false|null|default)$'),
    re.compile(r'^return\s+unchecked\(\(\w+\)\s*-?(?:0x[0-9A-Fa-f]+|\d+)\)$'),
    re.compile(r'^return\s+\(\w+\)\s*-?(?:0x[0-9A-Fa-f]+|\d+)$'),
    re.compile(r'^throw\s+new\s+\w*Exception\(.*\)$'),
]


def strip_comments(text):
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '/' and i + 1 < n and text[i + 1] == '/':
            j = text.find('\n', i)
            if j < 0:
                j = n
            i = j
        elif c == '/' and i + 1 < n and text[i + 1] == '*':
            j = text.find('*/', i + 2)
            i = n if j < 0 else j + 2
        elif c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                if text[j] == '\\':
                    j += 1
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                if text[j] == '\\':
                    j += 1
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def match_brace(text, i):
    """text[i] == '{' -> index of matching '}' (text has no comments/strings issues assumed)."""
    depth = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def classify_body(body):
    """body = text between braces (no comments). -> (status, nstmt)"""
    b = body.strip()
    if not b:
        return "STUB", 0
    # split statements on ';' and on braces (control flow makes it REAL anyway)
    if any(k in b for k in ('{', '}', 'if (', 'if(', 'for (', 'for(', 'while', 'switch', 'do ', 'else')):
        return "REAL", b.count(';') + 1
    stmts = [s.strip() for s in b.split(';') if s.strip()]
    for s in stmts:
        s1 = re.sub(r'\s+', ' ', s)
        if not any(p.match(s1) for p in STUB_STMT):
            return "REAL", len(stmts)
    return "STUB", len(stmts)


def parse_file(path):
    raw = open(path, encoding="utf-8-sig").read()
    lines = raw.split('\n')
    clean = strip_comments(raw)  # same length is NOT preserved; use line offsets on raw instead
    # Build offset table for raw lines
    offs = [0]
    for ln in lines:
        offs.append(offs[-1] + len(ln) + 1)
    # class name per line (last declared)
    cls_at = []
    cur = None
    for ln in lines:
        m = CLASS.search(ln)
        if m and not ln.strip().startswith('//'):
            cur = m.group(1)
        cls_at.append(cur)
    results = []  # (addr, name, info)
    ann_lines = [k for k, ln in enumerate(lines) if ANN.match(ln)]
    for idx, k in enumerate(ann_lines):
        text = ANN.match(lines[k]).group(1)
        pairs = PAIR.findall(text)
        if text.strip().lower().startswith("no symbol"):
            pairs = [("(no symbol)", p[1]) for p in pairs]
        if not pairs:
            continue
        suffix = None
        ms = re.search(r'\(([A-Z_0-9.]+)\)\s*$', text.strip())
        if ms:
            suffix = ms.group(1)
        limit = ann_lines[idx + 1] if idx + 1 < len(ann_lines) else len(lines)
        # scan forward for the first declaration
        j = k + 1
        info = {"file": os.path.relpath(path, ROOT).replace('\\', '/'), "cls": cls_at[k], "kind": "OTHER",
                "method": None, "status": None, "nstmt": 0, "line": k + 1, "suffix": suffix,
                "names": [p[0] for p in pairs]}
        found = None
        while j < limit:
            ln = lines[j].strip()
            if not ln or ln.startswith('//') or ln.startswith('['):
                j += 1
                continue
            if ln.startswith('#'):
                j += 1
                continue
            # accumulate a declaration until ';' or '{' or '=>' at depth 0
            decl = ''
            jj = j
            while jj < limit:
                decl += lines[jj] + '\n'
                d = strip_comments(decl)
                # find first of ';' or '{' or '=>' outside parentheses
                depth = 0
                pos = None
                kind = None
                for p, ch in enumerate(d):
                    if ch == '(':
                        depth += 1
                    elif ch == ')':
                        depth -= 1
                    elif depth == 0 and ch == ';':
                        pos, kind = p, ';'
                        break
                    elif depth == 0 and ch == '{':
                        pos, kind = p, '{'
                        break
                    elif depth == 0 and ch == '=' and p + 1 < len(d) and d[p + 1] == '>':
                        pos, kind = p, '=>'
                        break
                if pos is not None:
                    head = d[:pos]
                    break
                jj += 1
            else:
                break
            head1 = re.sub(r'\([^()]*(?:\([^()]*\)[^()]*)*\)', '()', head)  # collapse params
            is_method = '(' in head1 and '=' not in head1.split('(')[0] and not re.search(r'\bnew\b', head1) \
                and re.search(r'\b[A-Za-z_][A-Za-z0-9_<>\[\],\.]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(', head1) \
                and not re.match(r'^\s*(if|for|while|switch|return|foreach)\b', head1.strip())
            if kind == ';' and not is_method:
                info["kind"] = "FIELD"
                # keep scanning: a const may precede the method
                j = jj + 1
                continue
            if kind == ';' and is_method:
                # abstract/extern declaration -> treat as stub
                mm = re.search(r'([A-Za-z_][A-Za-z0-9_]*)\s*\($', head1.strip()[:head1.strip().rfind('(') + 1])
                info.update(kind="METHOD", method=mm.group(1) if mm else None, status="STUB", nstmt=0,
                            cls=cls_at[j])
                found = True
                break
            if kind == '{' and not is_method:
                # class/struct/enum/property/field-initializer -> skip block
                j = jj + 1
                # if it's a class declaration, keep scanning inside
                continue
            if kind == '=>' and is_method:
                mm = re.search(r'([A-Za-z_][A-Za-z0-9_]*)\s*\(', head1)
                # expression body
                tail = strip_comments('\n'.join(lines[j:limit]))
                epos = tail.find('=>')
                semi = tail.find(';', epos)
                expr = tail[epos + 2:semi].strip()
                st = "STUB" if re.match(r'^(-?\d+|0x[0-9A-Fa-f]+|true|false|null|default|throw .*)$', expr) else "REAL"
                info.update(kind="METHOD", method=mm.group(1) if mm else None, status=st, nstmt=1, cls=cls_at[j])
                found = True
                break
            if kind == '{' and is_method:
                mm = re.search(r'([A-Za-z_][A-Za-z0-9_]*)\s*\(', head1)
                tail = strip_comments('\n'.join(lines[j:]))
                bpos = tail.find('{', len(strip_comments(head)) - 2 if len(head) > 2 else 0)
                bpos = tail.find('{')
                # ensure we pick the brace after the signature: search from position of signature end
                sig_end = tail.find(')')
                # find the '{' that follows the last ')' of the signature at depth 0
                depth = 0
                bpos = -1
                for p, ch in enumerate(tail):
                    if ch == '(':
                        depth += 1
                    elif ch == ')':
                        depth -= 1
                    elif ch == '{' and depth == 0:
                        bpos = p
                        break
                epos = match_brace(tail, bpos)
                body = tail[bpos + 1:epos]
                st, ns = classify_body(body)
                info.update(kind="METHOD", method=mm.group(1) if mm else None, status=st, nstmt=ns, cls=cls_at[j])
                found = True
                break
            j = jj + 1
        for name, addr in pairs:
            results.append((int(addr, 16), name, dict(info)))
    return results


def load_overlay(tag):
    d = os.path.join(ROOT, OVERLAYS[tag])
    out = {}
    for path in sorted(glob.glob(os.path.join(d, "*.cs"))):
        for addr, name, info in parse_file(path):
            info = dict(info)
            info["ann_name"] = name
            out.setdefault(addr, []).append(info)
    return out


def load_all():
    return {tag: load_overlay(tag) for tag in OVERLAYS}


if __name__ == "__main__":
    allp = load_all()
    for tag, d in allp.items():
        meth = sum(1 for a, L in d.items() for i in L if i["kind"] == "METHOD")
        real = sum(1 for a, L in d.items() if any(i["kind"] == "METHOD" and i["status"] == "REAL" for i in L))
        stub = sum(1 for a, L in d.items() if any(i["kind"] == "METHOD" for i in L) and not any(i["kind"] == "METHOD" and i["status"] == "REAL" for i in L))
        bad = [(a, i) for a, L in d.items() for i in L if i["suffix"] and i["suffix"] != SUFFIX[tag]]
        print("%-6s annotated addrs=%d method-annots=%d REAL-addrs=%d STUB-addrs=%d suffix-mismatch=%d" % (
            tag, len(d), meth, real, stub, len(bad)))
        for a, i in bad[:10]:
            print("   suffix mismatch: %s:%d 0x%08X %s" % (i["file"], i["line"], a, i["suffix"]))
    if len(sys.argv) > 1:
        tag = sys.argv[1]
        for a in sorted(allp[tag]):
            for i in allp[tag][a]:
                if i["kind"] == "METHOD":
                    print("0x%08X\t%s\t%s\t%s\t%s\t%s\t%d\t%s:%d" % (a, i["ann_name"], i["status"], i["cls"], i["method"], i["kind"], i["nstmt"], i["file"], i["line"]))
