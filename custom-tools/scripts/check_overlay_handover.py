#!/usr/bin/env python3
"""Verifie l'invariant de bascule d'overlay du portage.

PsxRam ne tient QU'UN SEUL resolveur d'adresses installe. PsxSdkBridges l'echange par overlay.
Une bascule LoadExec qui n'echange pas le resolveur laisse celui de l'overlay SORTANT installe:
toute adresse de l'overlay ENTRANT ne correspond a rien, chaque lecture rend zero, chaque ecriture
est jetee, et rien ne le signale. Le build reste vert et les bancs aussi, parce qu'aucun banc
n'emprunte le chemin de jeu — les bancs appellent Activate* eux-memes.

Ce defaut s'est produit deux fois. La premiere, PsxSdkBridges n'avait aucune entree pour VS.EXE et
trois tranches de portage etaient inertes. La seconde, l'entree existait et etait appelee par le
banc, mais SELECT_EXE/OverlayExit.LoadExec ne l'appelait pas: le chemin de JEU vers VS.EXE restait
inerte, derriere un commentaire disant « aucune des trois cibles n'est transliteree » qui etait
vrai a l'ecriture et faux trois commits plus tard.

L'invariant verifie ici: pour chaque cible passee a un ShutdownAndLoadExecutable du portage, si
cette cible EST transliteree (il existe un dossier <CIBLE>_EXE avec un ResolveAddress), alors le
LoadExec qui la recoit doit installer son resolveur.

Usage:  python custom-tools/scripts/check_overlay_handover.py
Sortie: 0 si l'invariant tient, 1 sinon.
"""
import os, re, sys

ROOT = os.path.join("custom-tools", "DbzLegendsAnalyser", "DbzLegendsRemaster")

TARGET = re.compile(r'ShutdownAndLoadExecutable\([^)]*?([A-Z0-9_]+)\.EXE')  # pas d'antislash litteral dans le motif: le nom de fichier suffit
ACTIVATE = re.compile(r'PsxSdkBridges\.(Activate\w+Exe)\s*\(')
BRIDGE = re.compile(r'internal static void (Activate\w+Exe)\s*\(')

def strip_comments(text):
    return "\n".join("" if l.strip().startswith("//") else l for l in text.split("\n"))

def read(path):
    return strip_comments(open(path, encoding="utf-8-sig").read())

# quels overlays sont transliteres (dossier + ResolveAddress) ?
ported = {}
for name in sorted(os.listdir(ROOT)):
    d = os.path.join(ROOT, name)
    if not os.path.isdir(d) or not name.endswith("_EXE"):
        continue
    for f in os.listdir(d):
        if f.endswith(".cs") and "ResolveAddress" in read(os.path.join(d, f)):
            ported[name[:-4].replace("_", ".")] = name
            break

# quels Activate* existent dans le pont ?
bridge_path = os.path.join(ROOT, "PsxSdkBridges.cs")
bridges = set(BRIDGE.findall(read(bridge_path))) if os.path.exists(bridge_path) else set()

print("overlays transliteres : " + ", ".join(sorted(ported)) )
print("bascules disponibles  : " + ", ".join(sorted(bridges)))
print()

failures = 0
for dirpath, _dirs, files in os.walk(ROOT):
    for f in sorted(files):
        if not f.endswith(".cs"):
            continue
        path = os.path.join(dirpath, f)
        text = read(path)
        targets = TARGET.findall(text)
        if not targets:
            continue
        # LE RESOLVEUR EST INSTALLE PAR LE FICHIER QUI IMPLEMENTE LoadExec, PAS PAR CELUI QUI
        # APPELLE ShutdownAndLoadExecutable. SELECT_EXE/ModeBranches.cs demande la bascule et
        # SELECT_EXE/OverlayExit.cs la realise; TITLE_EXE/TitleScreenTask.cs demande et
        # TITLE_EXE/FrameLoop.cs realise. Chercher dans le seul fichier appelant declarait donc
        # deux violations qui n'en sont pas. La portee juste est le DOSSIER de l'overlay sortant.
        installed = set()
        for sibling in sorted(os.listdir(dirpath)):
            if sibling.endswith(".cs"):
                installed |= set(ACTIVATE.findall(read(os.path.join(dirpath, sibling))))
        rel = os.path.relpath(path, ROOT).replace("\\", "/")
        for t in sorted(set(targets)):
            expected = "Activate" + t.replace(".", "").capitalize() + "Exe"
            match = next((b for b in bridges if b.lower() == expected.lower()), None)
            if t not in ported:
                print("    BLOCKED  %-12s <- %-28s (cible non transliteree)" % (t, rel))
            elif match and match in installed:
                print("    OK       %-12s <- %-28s (%s)" % (t, rel, match))
            else:
                print("    ECHEC    %-12s <- %-28s  <-- transliteree, mais ce fichier"
                      " n'installe pas son resolveur: le resolveur sortant reste en place et"
                      " tout l'overlay entrant lira zero" % (t, rel))
                failures += 1

print()
print("invariant tenu" if failures == 0 else "%d violation(s)" % failures)
sys.exit(1 if failures else 0)
