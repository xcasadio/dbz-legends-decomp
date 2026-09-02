#!/usr/bin/env python3
"""Verifie l'invariant de repartition des taches du portage.

CreateTask range une ADRESSE PSX brute dans le noeud a +0x04, exactement comme l'original.
TaskSystem.ExecuteTaskList retrouve le corps porte via un dictionnaire d'enregistrement, et
IGNORE EN SILENCE une adresse qu'il ne connait pas. Un noeud cree sans rappel enregistre est
donc du code compile, vivant dans la liste, et jamais execute — sans erreur, sans avertissement,
avec un build vert.

Ce defaut s'est produit trois fois pendant le portage de VS.EXE, chaque fois a la couture entre
deux tranches ecrites en parallele: le corps des combattants, celui du gestionnaire de combat, et
le consommateur du roster. Chacune des deux tranches concernees avait raison sur sa moitie.

L'invariant verifie ici: pour chaque CreateTask(E, ...), soit E a un rappel enregistre, soit E
n'a aucun corps porte (auquel cas l'absence d'enregistrement est le BLOCKED attendu).

Usage:  python custom-tools/scripts/check_task_registration.py
Sortie: 0 si l'invariant tient, 1 sinon.
"""
import os, re, sys

ROOT = os.path.join("custom-tools", "DbzLegendsAnalyser", "DbzLegendsRemaster")
OVERLAYS = ["VS_EXE", "TITLE_EXE", "SELECT_EXE"]

CONST = re.compile(r'const\s+int\s+(\w+)\s*=\s*unchecked\(\(int\)(0x[0-9A-Fa-f]+)\)')
CREATE = re.compile(r'TaskSystem\.CreateTask\(\s*([\w.]+)', re.S)
REGIST = re.compile(r'TaskSystem\.RegisterCallback\(\s*([\w.]+)', re.S)
GHIDRA = re.compile(r'//\s*GHIDRA:.*@\s*(0x[0-9A-Fa-f]{8})')
METHOD = re.compile(r'^\s*(?:internal|private|public)\s+static\s+[\w\[\]<>?]+\s+\w+\s*\(')

def scan(overlay):
    d = os.path.join(ROOT, overlay)
    consts, created, registered, bodies = {}, [], set(), set()
    for name in sorted(os.listdir(d)):
        if not name.endswith(".cs"):
            continue
        lines = open(os.path.join(d, name), encoding="utf-8-sig").read().split("\n")
        for i, l in enumerate(lines):
            m = CONST.search(l)
            if m:
                consts[m.group(1)] = int(m.group(2), 16)
                consts["%s.%s" % (name[:-3], m.group(1))] = int(m.group(2), 16)
            m = GHIDRA.search(l)
            if m:
                # un corps porte, pas seulement une constante d'adresse
                for j in range(i + 1, min(i + 12, len(lines))):
                    if METHOD.match(lines[j]):
                        bodies.add(int(m.group(1), 16) & 0xFFFFFFFF)
                        break
                    if lines[j].strip() and not lines[j].strip().startswith("//"):
                        break
    # CreateTask / RegisterCallback sont cherches sur le TEXTE ENTIER: l'argument d'entree passe
    # parfois a la ligne suivante (FighterSetup), et une recherche ligne a ligne le manque.
    for name in sorted(os.listdir(d)):
        if not name.endswith(".cs"):
            continue
        # LES LIGNES DE COMMENTAIRE SONT RETIREES AVANT LA RECHERCHE, et c'est le temoin negatif
        # qui l'a impose: en commentant l'enregistrement du gestionnaire de combat pour verifier que
        # ce script tombe bien, il ne tombait pas — il comptait le nom dans la ligne commentee. Un
        # verificateur qui ne peut pas echouer ne prouve rien.
        raw_text = open(os.path.join(d, name), encoding="utf-8-sig").read()
        text = chr(10).join("" if l.strip().startswith("//") else l
                            for l in raw_text.split(chr(10)))
        for m in CREATE.finditer(text):
            created.append((name, text[:m.start()].count(chr(10)) + 1, m.group(1)))
        for m in REGIST.finditer(text):
            registered.add(m.group(1))
    return consts, created, registered, bodies

failures = 0
for overlay in OVERLAYS:
    consts, created, registered, bodies = scan(overlay)
    print("=== %s : %d CreateTask, %d rappels enregistres" % (overlay, len(created), len(registered)))
    # LA COMPARAISON SE FAIT SUR L'ADRESSE, PAS SUR LE NOM. Le meme point d'entree porte deux noms
    # dans le portage — main l'epelle Lab80055e3cAddress et BattleState l'epelle BattleManagerEntry —
    # et une comparaison par nom declarait a tort une violation.
    reg_addrs = {consts.get(r, consts.get(r.split(".")[-1])) for r in registered}
    reg_names = {r.split(".")[-1] for r in registered}
    for fname, line, expr in created:
        short = expr.split(".")[-1]
        addr = consts.get(expr, consts.get(short))
        ok = short in reg_names or (addr is not None and addr in reg_addrs)
        if ok:
            print("    OK       %-22s %s:%d" % (expr, fname, line))
        elif addr is not None and (addr & 0xFFFFFFFF) not in bodies:
            print("    BLOCKED  %-22s %s:%d  (aucun corps porte, absence attendue)"
                  % (expr, fname, line))
        else:
            print("    ECHEC    %-22s %s:%d  <-- corps porte, rappel NON enregistre:"
                  " la liste parcourra un noeud vivant et ne distribuera rien"
                  % (expr, fname, line))
            failures += 1
print()
print("invariant tenu" if failures == 0 else "%d violation(s)" % failures)
sys.exit(1 if failures else 0)
