"""Minimal MIPS I disassembler (enough for side-by-side checks)."""
REG = ["zero", "at", "v0", "v1", "a0", "a1", "a2", "a3", "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
       "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7", "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"]
SPECIAL = {0: "sll", 2: "srl", 3: "sra", 4: "sllv", 6: "srlv", 7: "srav", 8: "jr", 9: "jalr", 0xC: "syscall",
           0xD: "break", 0x10: "mfhi", 0x11: "mthi", 0x12: "mflo", 0x13: "mtlo", 0x18: "mult", 0x19: "multu",
           0x1A: "div", 0x1B: "divu", 0x20: "add", 0x21: "addu", 0x22: "sub", 0x23: "subu", 0x24: "and",
           0x25: "or", 0x26: "xor", 0x27: "nor", 0x2A: "slt", 0x2B: "sltu"}
ITYPE = {4: "beq", 5: "bne", 6: "blez", 7: "bgtz", 8: "addi", 9: "addiu", 0xA: "slti", 0xB: "sltiu", 0xC: "andi",
         0xD: "ori", 0xE: "xori", 0xF: "lui", 0x20: "lb", 0x21: "lh", 0x22: "lwl", 0x23: "lw", 0x24: "lbu",
         0x25: "lhu", 0x26: "lwr", 0x28: "sb", 0x29: "sh", 0x2A: "swl", 0x2B: "sw", 0x2E: "swr", 0x32: "lwc2",
         0x3A: "swc2"}
LOADSTORE = {0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x28, 0x29, 0x2A, 0x2B, 0x2E, 0x32, 0x3A}


def simm(w):
    v = w & 0xFFFF
    return v - 0x10000 if v & 0x8000 else v


def dis(w, pc):
    if w == 0:
        return "nop"
    op = (w >> 26) & 0x3F
    rs, rt, rd, sa, f = (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, w & 0x3F
    if op == 0:
        m = SPECIAL.get(f, "special?%02x" % f)
        if f in (0, 2, 3):
            return "%s %s,%s,%d" % (m, REG[rd], REG[rt], sa)
        if f in (4, 6, 7):
            return "%s %s,%s,%s" % (m, REG[rd], REG[rt], REG[rs])
        if f == 8:
            return "jr %s" % REG[rs]
        if f == 9:
            return "jalr %s,%s" % (REG[rd], REG[rs])
        if f in (0xC, 0xD):
            return m
        if f in (0x10, 0x12):
            return "%s %s" % (m, REG[rd])
        if f in (0x11, 0x13):
            return "%s %s" % (m, REG[rs])
        if f in (0x18, 0x19, 0x1A, 0x1B):
            return "%s %s,%s" % (m, REG[rs], REG[rt])
        return "%s %s,%s,%s" % (m, REG[rd], REG[rs], REG[rt])
    if op == 1:
        m = {0: "bltz", 1: "bgez", 0x10: "bltzal", 0x11: "bgezal"}.get(rt, "regimm?")
        return "%s %s,0x%08X" % (m, REG[rs], pc + 4 + simm(w) * 4)
    if op in (2, 3):
        return "%s 0x%08X" % ("j" if op == 2 else "jal", ((pc + 4) & 0xF0000000) | ((w & 0x03FFFFFF) << 2))
    if op in (4, 5):
        return "%s %s,%s,0x%08X" % (ITYPE[op], REG[rs], REG[rt], pc + 4 + simm(w) * 4)
    if op in (6, 7):
        return "%s %s,0x%08X" % (ITYPE[op], REG[rs], pc + 4 + simm(w) * 4)
    if op == 0xF:
        return "lui %s,0x%04X" % (REG[rt], w & 0xFFFF)
    if op in (0xC, 0xD, 0xE):
        return "%s %s,%s,0x%04X" % (ITYPE[op], REG[rt], REG[rs], w & 0xFFFF)
    if op in (8, 9, 0xA, 0xB):
        return "%s %s,%s,%d" % (ITYPE[op], REG[rt], REG[rs], simm(w))
    if op in LOADSTORE:
        return "%s %s,%d(%s)" % (ITYPE[op], REG[rt] if op < 0x30 else "c2r%d" % rt, simm(w), REG[rs])
    if op == 0x10:
        return "cop0 0x%08X" % w
    if op == 0x12:
        if rs == 0:
            return "mfc2 %s,c2d%d" % (REG[rt], rd)
        if rs == 2:
            return "cfc2 %s,c2c%d" % (REG[rt], rd)
        if rs == 4:
            return "mtc2 %s,c2d%d" % (REG[rt], rd)
        if rs == 6:
            return "ctc2 %s,c2c%d" % (REG[rt], rd)
        return "cop2 0x%07X" % (w & 0x1FFFFFF)
    return ".word 0x%08X" % w
