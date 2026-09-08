"""Derive a record's field map from the image, instead of guessing it offset by offset.

WHY THIS EXISTS. The VS.EXE port models every game record -- fighters, attack events,
task nodes, the battle context -- as a bare `int` address with magic offsets written at
each access site. That works, and it is what the port contract asks for while the
evidence is open, but it has a cost the project has now paid twice: nothing in the code
says how big a record is, which offsets are fields, how wide each one is, or whether it
is signed. Two consequences, both real:

  * a static sweep for "writes to +0x134" counted three stack saves (`sw ...,308($sp)`)
    as fighter writes, because an offset alone cannot tell a record from a stack frame;
  * a function's signature stayed wrong (FUN_8004EE48 took one parameter where the image
    passes four) because nothing typed the arguments, and the three extra ones turned out
    to carry the hit position.

So: give this tool a function and say which argument register holds the record. It walks
the instructions, tracks every register that provably holds "record base + K" -- through
register moves, `addiu` offsets, and stack spill/reload pairs -- and reports every load
and store made through one, with its offset, its width, and whether the load was signed.
The union over a set of functions that share a record type is that record's field map.

Nothing here is inferred from the C# port: the port is what this checks.

Usage:
  python struct_fields.py --func 0x8004a97c:a0 --func 0x8004aa44:a0 [--name FighterRecord]
  python struct_fields.py --preset fighter
  python struct_fields.py --preset attack-event
"""

import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
IMAGE = ROOT / "data" / "VS.EXE"
LOAD_ADDRESS = 0x80020000
HEADER_SIZE = 0x800

REG = ['zero', 'at', 'v0', 'v1', 'a0', 'a1', 'a2', 'a3', 't0', 't1', 't2', 't3', 't4', 't5', 't6', 't7',
       's0', 's1', 's2', 's3', 's4', 's5', 's6', 's7', 't8', 't9', 'k0', 'k1', 'gp', 'sp', 's8', 'ra']
RNUM = {name: i for i, name in enumerate(REG)}

# opcode -> (mnemonic, width in bytes, signed?)
LOADS = {0x20: ('lb', 1, True), 0x21: ('lh', 2, True), 0x23: ('lw', 4, True),
         0x24: ('lbu', 1, False), 0x25: ('lhu', 2, False),
         0x22: ('lwl', 4, False), 0x26: ('lwr', 4, False)}
STORES = {0x28: ('sb', 1), 0x29: ('sh', 2), 0x2b: ('sw', 4), 0x2a: ('swl', 4), 0x2e: ('swr', 4)}

BLOB = IMAGE.read_bytes()
NWORDS = (len(BLOB) - HEADER_SIZE) // 4


def word(addr):
    off = addr - LOAD_ADDRESS + HEADER_SIZE
    if off < 0 or off + 4 > len(BLOB):
        return None
    return struct.unpack_from('<I', BLOB, off)[0]


def signed16(v):
    return v - 0x10000 if v & 0x8000 else v


def function_end(start, limit=0x2000):
    """First `jr ra` at or after start, plus its delay slot."""
    for a in range(start, start + limit, 4):
        w = word(a)
        if w is None:
            break
        if w == 0x03E00008:
            return a + 8
    return start + limit


def scan(start, arg_reg, fields, pointers, warnings):
    """Walk one function, tracking registers that hold `record base + K`.

    THE CODE IS BUILT AT -O0 AND THAT IS THE WHOLE DIFFICULTY. A function does not keep
    its argument in $a0: it spills it to the frame immediately (`sw $a0,0x20($s8)` at
    0x8004EE58) and reloads it before every single use, through the FRAME POINTER $s8
    rather than $sp. A tracker that follows only $sp sees nothing at all -- the first
    version of this tool reported an empty map for a 1292-byte function. So two maps are
    kept: `base`, registers holding record+K, and `frame`, registers holding the stack
    frame (sp and any alias of it), which is what makes spill/reload pairs followable.
    """
    end = function_end(start)
    # reg -> offset from the record base
    base = {RNUM[arg_reg]: 0}
    # reg -> offset from the frame base, so $sp and its aliases (typically $s8) agree
    frame = {29: 0}
    # frame slot -> offset from the record base
    slots = {}

    for a in range(start, end, 4):
        w = word(a)
        if w is None:
            return
        op = w >> 26
        rs, rt, rd, sh, fn = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, w & 63
        imm = w & 0xffff

        # ---- loads and stores
        if op in LOADS or op in STORES:
            disp = signed16(imm)
            if rs in base:
                off = base[rs] + disp
                if op in LOADS:
                    name, width, signed = LOADS[op]
                    fields.setdefault(off, []).append(('R', width, signed, name, a))
                    if width == 4 and op == 0x23:
                        pointers.setdefault(off, set()).add(a)
                else:
                    name, width = STORES[op]
                    fields.setdefault(off, []).append(('W', width, None, name, a))

            if op in LOADS:
                if rs in frame and (frame[rs] + disp) in slots:
                    base[rt] = slots[frame[rs] + disp]      # reload of a spilled base
                else:
                    base.pop(rt, None)
                frame.pop(rt, None)
            else:
                if rs in frame and rt in base:
                    slots[frame[rs] + disp] = base[rt]      # spill of a tracked base
            continue

        # ---- register moves and offsets keep both maps alive
        if op == 0 and fn in (0x21, 0x25):          # addu / or  (a move when one side is zero)
            if rd == 0:
                continue
            src = srcf = None
            if rt == 0 and rs in base:
                src = base[rs]
            elif rs == 0 and rt in base:
                src = base[rt]
            if rt == 0 and rs in frame:
                srcf = frame[rs]
            elif rs == 0 and rt in frame:
                srcf = frame[rt]
            base[rd] = src if src is not None else base.pop(rd, None)
            if src is None:
                base.pop(rd, None)
            if srcf is not None:
                frame[rd] = srcf
            else:
                frame.pop(rd, None)
            continue

        if op == 0x09:                               # addiu rt, rs, imm
            if rs in base:
                base[rt] = base[rs] + signed16(imm)
            else:
                base.pop(rt, None)
            if rs in frame and rt != 29:
                frame[rt] = frame[rs] + signed16(imm)
            elif rt != 29:
                frame.pop(rt, None)
            continue

        # ---- anything else that writes a register kills it in both maps
        if op == 0:
            if fn in (0x08,):                        # jr
                continue
            if fn in (0x18, 0x19, 0x1a, 0x1b):       # mult/div write hi/lo only
                continue
            base.pop(rd, None)
            frame.pop(rd, None)
        elif op in (0x0f, 0x08, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e):
            base.pop(rt, None)
            frame.pop(rt, None)
        elif op == 3:                                # jal clobbers the caller-saved set
            for r in (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 24, 25, 31):
                base.pop(r, None)
                frame.pop(r, None)


# NAMES THE PORT HAS ALREADY CLOSED WITH EVIDENCE. Everything else stays field_0xNN:
# rule 6 of the port contract says keep a raw name until the proof is decisive, and an
# offset that is merely busy is not proof of meaning.
KNOWN_NAMES = {
    'FighterRecord': {
        0x04: 'animFrameCounter',      # the `+4 halfword == 0` guard every action arm tests
        0xac: 'currentTaskNode',       # written beside +0x22a, compared against g_CurrentTask
        0x134: 'flagsB',
        0x138: 'flagsA',               # 244 accesses; the word the whole action machine turns on
        0x144: 'characterData',        # ActivateFighterInSlot is its only non-zero writer
        0x16a: 'stateOpcode',          # what FighterSetState stamps
        0x16b: 'moveClass',            # the six-way switch in FUN_800261EC
        0x173: 'slotIndex',            # 0..5 team A, 6..11 team B
        0x22c: 'inputFlags',
        0x22d: 'archetypeIndex',       # indexes PTR_DAT_800807A4
    },
    'AttackEventRecord': {
        0x3c: 'attackerTaskNode',      # one +8 hop reaches the acting fighter
        0x6c: 'eventType',             # >>8 == 0x80 is the alternate-target form
        0x70: 'targetTaskNode',        # +0xc then +8 reaches the target fighter
        0x78: 'eventFlags',            # negative is what opens FUN_8004EE48
    },
}


def c_type(width, sign):
    # 'inconnu' means the field is only ever stored to in the scanned set, so nothing says
    # how it is meant to be read. Ghidra's `undefined` widths say exactly that.
    if sign == 'inconnu':
        return {1: 'undefined1', 2: 'undefined2', 4: 'undefined4'}[width]
    if width == 1:
        return 'char' if sign == 'signe' else 'byte'
    if width == 2:
        return 'short' if sign == 'signe' else 'ushort'
    return 'int' if sign == 'signe' else 'uint'


def emit_c(name, fields):
    """Render the field map as a Ghidra-parseable C structure.

    Two shapes need care. UNALIGNED PAIRS: an `lwl`/`lwr` pair at +0x53 and +0x50 is ONE
    word at +0x50, and the tracker honestly reports both addresses, so any offset that
    falls inside the previous field's extent is folded away rather than emitted. MIXED
    WIDTHS: an offset read as both a byte and a word takes the widest, because the
    structure has to hold the largest access; the narrower ones are noted as comments so
    the fact is not lost.
    """
    lines = ['struct %s {' % name]
    cursor = 0
    for off in sorted(fields):
        if off < cursor:
            continue                       # folded into the field that already covers it
        entries = fields[off]
        widths = sorted({e[1] for e in entries})
        signs = {e[2] for e in entries if e[0] == 'R'}
        if signs == {True}:
            sign = 'signe'
        elif signs == {False}:
            sign = 'non signe'
        elif not signs:
            sign = 'inconnu'          # written and never read: no evidence of signedness
        else:
            sign = 'mixte'
        width = widths[-1]
        if off > cursor:
            lines.append('    undefined1 pad_%x[%d];' % (cursor, off - cursor))
        nm = KNOWN_NAMES.get(name, {}).get(off) or ('field_%x' % off)
        note = ''
        if len(widths) > 1:
            note = '  /* also read %s bytes wide */' % '/'.join(str(x) for x in widths[:-1])
        elif sign == 'mixte':
            note = '  /* read both signed and unsigned */'
        elif sign == 'inconnu':
            note = '  /* only ever written here: signedness not evidenced */'
        lines.append('    %-8s %s;%s' % (c_type(width, sign), nm, note))
        cursor = off + width
    lines.append('};')
    return chr(10).join(lines)


PRESETS = {
    # A fighter record in $a0. Seeded with functions whose FIRST argument the port and
    # Ghidra agree is a fighter: the state setters, the step-9.4 dispatchers, the AI, and
    # the two gauge roots' own neighbours.
    'fighter': ('FighterRecord', [
        (0x8004a910, 'a0'), (0x8004a97c, 'a0'), (0x8004aa44, 'a0'), (0x8004ad0c, 'a0'),
        (0x8004b024, 'a0'), (0x8004b098, 'a0'), (0x8004b33c, 'a0'), (0x8004b8a0, 'a0'),
        (0x8004b9cc, 'a0'), (0x8004bb70, 'a0'), (0x8004bd3c, 'a0'), (0x8004bf50, 'a0'),
        (0x8004c198, 'a0'), (0x8004a9e8, 'a0'), (0x80023890, 'a0'), (0x800261ec, 'a0'),
        (0x8004e758, 'a0'), (0x8004d574, 'a0'), (0x8004e580, 'a0'),
    ]),
    # The attack-event record. FUN_8004EE48 takes it in $a0; UpdateAttackEventTask reaches
    # the same record through the current task's context, which this cannot follow, so the
    # map here is what one function sees.
    'attack-event': ('AttackEventRecord', [
        (0x8004ee48, 'a0'),
    ]),
}


def main():
    argv = sys.argv[1:]
    if not argv:
        print(__doc__)
        return 2

    name = 'Record'
    funcs = []
    emit = False
    i = 0
    while i < len(argv):
        if argv[i] == '--preset' and i + 1 < len(argv):
            key = argv[i + 1]
            if key not in PRESETS:
                print('preset inconnu: %s (connus: %s)' % (key, ', '.join(PRESETS)))
                return 2
            name, funcs = PRESETS[key]
            i += 2
        elif argv[i] == '--func' and i + 1 < len(argv):
            addr, _, reg = argv[i + 1].partition(':')
            funcs.append((int(addr, 16), reg or 'a0'))
            i += 2
        elif argv[i] == '--name' and i + 1 < len(argv):
            name = argv[i + 1]
            i += 2
        elif argv[i] == '--emit-c':
            emit = True
            i += 1
        else:
            i += 1

    if not funcs:
        print(__doc__)
        return 2

    fields = {}
    pointers = {}
    warnings = set()
    for addr, reg in funcs:
        scan(addr, reg, fields, pointers, warnings)

    if emit:
        print(emit_c(name, fields))
        return 0

    print('%s -- carte des champs, %d fonction(s) balayee(s)' % (name, len(funcs)))
    print('derivee de data/VS.EXE, pas du portage')
    print()
    print('%-8s %-6s %-9s %-7s %s' % ('offset', 'largeur', 'signe', 'acces', 'sites'))
    print('-' * 62)
    negative = [o for o in fields if o < 0]
    for off in sorted(fields):
        entries = fields[off]
        widths = sorted({e[1] for e in entries})
        signs = {e[2] for e in entries if e[0] == 'R'}
        if signs == {True}:
            sign = 'signe'
        elif signs == {False}:
            sign = 'non signe'
        elif not signs:
            sign = '-'
        else:
            sign = 'LES DEUX'
        reads = sum(1 for e in entries if e[0] == 'R')
        writes = sum(1 for e in entries if e[0] == 'W')
        acc = ('L%d' % reads if reads else '') + ('/' if reads and writes else '') + ('E%d' % writes if writes else '')
        flag = '  <-- LARGEURS MELANGEES' if len(widths) > 1 else ''
        print('0x%-6x %-6s %-9s %-7s %d%s' % (
            off, '/'.join(str(x) for x in widths), sign, acc, len(entries), flag))

    print()
    print('champs lus comme des mots et donc candidats POINTEURS : %s'
          % (', '.join('0x%x' % o for o in sorted(pointers)) or 'aucun'))
    if negative:
        print('OFFSETS NEGATIFS (le suivi a derape, ou la base est un sous-enregistrement) : %s'
              % ', '.join('0x%x' % o for o in sorted(negative)))
    print('taille minimale impliquee : 0x%x octets' % (max(fields) + max(e[1] for e in fields[max(fields)])))
    if warnings:
        print()
        print('%d avertissement(s) de suivi (le premier appel coupe le suivi des registres temporaires)'
              % len(warnings))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
