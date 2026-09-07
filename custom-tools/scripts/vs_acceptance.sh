#!/usr/bin/env bash
# Acceptance loop for the VS.EXE port. Run from the repository root.
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
PROJ=custom-tools/DbzLegendsAnalyser/DbzLegendsRemaster
SLN=custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyser.slnx

echo "=== build ==="
if ! dotnet build "$SLN" -v q 2>&1 | grep -E "^ *[0-9]+ Erreur|error CS" | tail -5; then :; fi
BUILD_ERR=$(dotnet build "$SLN" -v q 2>&1 | grep -cE "error CS")
echo "build errors: $BUILD_ERR"

echo "=== benches ==="
BENCHES="--validate-digit-quads --validate-exe-image --validate-gte-rotavg --validate-heap --validate-pad-input --validate-pad-mute --validate-render --validate-sortsprite --validate-sound-loader --validate-tasks --validate-title-images --validate-title-init --validate-title-task --validate-vs-ram"
PASS=0; TOTAL=0
for b in $BENCHES; do
  TOTAL=$((TOTAL+1))
  if dotnet run --project "$PROJ" --no-build -- "$b" >/dev/null 2>&1; then
    PASS=$((PASS+1))
  else
    echo "  ECHEC $b"
  fi
done
echo "benches: $PASS/$TOTAL"

echo "=== seam checkers ==="
SEAM=0
for s in check_duplicate_symbols check_overlay_handover check_task_registration check_vs_dispatch check_function_addresses; do
  OUT=$(python custom-tools/scripts/$s.py 2>&1)
  if echo "$OUT" | grep -qiE "invariant tenu|TABLE CONFORME|stockages contenus dans une region: 0" && ! echo "$OUT" | grep -qi "invariant ROMPU"; then
    SEAM=$((SEAM+1))
  else
    echo "  ECHEC $s"; echo "$OUT" | tail -5
  fi
done
echo "seam: $SEAM/5"

echo "=== diag-select 400 (temoin de non-regression: 49396) ==="
dotnet run --project "$PROJ" --no-build -- --diag-select 400 2>&1 | grep -E "VRAM page0"

# The VS.EXE round-start chain, end to end. The two environment variables are the
# measurement, not a detail: DBZ_PAD_PRESS_MASK=0x0800 is R1 (PSX pad bit 11), the
# button that drives the round-start override at 0x80056358, and frame 300 is early
# enough to leave 600 of the 900 frames inside the round. Every phase count below is
# linear in that frame, so changing either number changes every number printed.
echo "=== diag-vs 900, R1 @ frame 300 (temoins: phases 2..9 = 1200, sprites 15120/7198) ==="
DBZ_PAD_PRESS_MASK=0x0800 DBZ_PAD_PRESS_FRAME=300 \
  dotnet run --project "$PROJ" --no-build -- --diag-vs 900 2>&1 \
  | grep -E "LES PHASES|pad port|DESSINEUR DE SPRITES"
