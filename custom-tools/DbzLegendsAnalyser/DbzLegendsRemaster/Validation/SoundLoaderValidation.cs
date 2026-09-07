using System;
using DbzLegendsRemaster.VS_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: pins the eight-state transition table of SoundDriver.SoundCdLoadStep @ 0x8005F704, the
// CD-load step machine the battle scene polls until it returns 8 or more.
//
// WHY IT NEEDS A BENCH AT ALL. That function was transliterated from raw disassembly, not from a
// decompilation: there was no Ghidra this session, so the control flow was read instruction by
// instruction out of PCSX-Redux. Nothing checks a state machine written that way except a test
// that walks it. A single mis-transcribed branch would show up as the loader silently stalling or
// silently skipping a step, months later, with nothing to point at.
//
// WHAT IT ASSERTS, and what it deliberately does not. It asserts the TRANSITIONS the disassembly
// proves: which state follows which, which conditions hold a state, which side effects each step
// leaves in the workspace. It does NOT assert that the loader completes -- it cannot, because
// three of the machine's callees (SsVabOpenHeadSticky, SsVabTransBody, SsVabTransCompleted) are
// unimplemented stubs returning 0 in PsxSdkMonogame.LibSnd. The stall that produces is itself
// asserted below, as a fact about today's port rather than as a defect of this function.
internal static class SoundLoaderValidation
{
    // A workspace of the real size at an address nothing else in the port claims, so the machine
    // reads and writes real bytes. The console allocates this from the task heap; the bench does
    // not need to model that to exercise the state table.
    private const int WorkspaceAddress = unchecked((int)0x801A0000);

    private static int s_failures;

    internal static int Run()
    {
        s_failures = 0;

        // The resolver has to be installed before any PsxRam access resolves; without it the
        // region below exists as a C# array that nothing can reach by address, and every read
        // answers a default. The first version of this bench omitted these two lines and failed
        // five assertions that were about its own setup, not about the state machine.
        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateVsExe();

        byte[] workspace = LibGpu.RamRegion(WorkspaceAddress, SoundState.WorkspaceSize);
        Array.Clear(workspace, 0, workspace.Length);
        SoundState.DAT_8008d284 = WorkspaceAddress;

        // ---- state 0 holds while the gate is non-zero, and only then advances.
        PsxRam.WriteU16(WorkspaceAddress + SoundState.Gate12A, 1);
        Check(FUN(0, 0) == 0, "etat 0 tenu tant que la porte +0x12A est non nulle");

        PsxRam.WriteU16(WorkspaceAddress + SoundState.Gate12A, 0);
        BattleScene.DAT_8008d340 = 0xFFFFFFFF;
        Check(FUN(0, 0) == 1, "etat 0 -> 1 une fois la porte a zero");
        Check(BattleScene.DAT_8008d340 == 0xFFFFFFF3,
            $"etat 0 efface les bits 0xC de gp+0x244, lu 0x{BattleScene.DAT_8008d340:X8}");

        // ---- state 1 releases all six voices and advances. Six, not five: the loop bound is
        // `slti v0,6` at 0x8005F7E0. Seeding the sixth slot is what makes this discriminating --
        // a five-iteration transcription would leave it untouched.
        for (int i = 0; i < SoundState.VoiceSlotCount; i++)
        {
            PsxRam.WriteU16(WorkspaceAddress + SoundState.VoiceHandles + i * 2, 0x0007);
            PsxRam.WriteU16(WorkspaceAddress + SoundState.VoiceFlags + i * 2, 0x0001);
        }

        Check(FUN(1, 1) == 2, "etat 1 -> 2");

        bool allReleased = true;
        bool allFlagged = true;
        for (int i = 0; i < SoundState.VoiceSlotCount; i++)
        {
            if (PsxRam.ReadU16(WorkspaceAddress + SoundState.VoiceHandles + i * 2) != 0xFFFF)
            {
                allReleased = false;
            }

            if ((PsxRam.ReadU16(WorkspaceAddress + SoundState.VoiceFlags + i * 2) & 0x80) == 0)
            {
                allFlagged = false;
            }
        }

        Check(allReleased, "etat 1 met les SIX poignees de voix a -1, la sixieme comprise");
        Check(allFlagged, "etat 1 pose le bit 0x80 sur les SIX drapeaux");
        Check(PsxRam.ReadU16(WorkspaceAddress + SoundState.RetryCountdown) == 0x0A,
            "etat 1 arme le compte a rebours a 10");

        // ---- THE WALK. This is the assertion that changed when Ghidra named the callees. The
        // first version of this bench expected state 2 to HOLD, because the poll was bound to an
        // unnamed FUN_ stub returning 0. Ghidra showed that poll is CdSync, which the SDK really
        // implements and which returns CdlComplete (2) -- so state 2 takes the ready path and
        // advances. The bench caught the change, which is what it is for.
        //
        // The libcd half is real, so the machine now walks for real: CdSync answers 2, CdReadSync
        // answers 0, and each step issues a genuine CdlSetloc / CdRead.
        Check(FUN(0, 2) == 3, "etat 2 -> 3: CdSync rend CdlComplete, la lecture est lancee");
        Check(FUN(0, 3) == 4, "etat 3 -> 4: CdReadSync rend 0, la lecture est finie");
        Check(FUN(0, 4) == 5, "etat 4 -> 5");
        Check(FUN(0, 5) == 7,
            "etat 5 -> 7: les deux appels VAB sont des stubs qui rendent 0, donc >= 0");

        // The workspace really was seeked into: CdIntToPos writes through the pointer, so +0xD8
        // holds a position rather than the zeros it started at. This is what proves the CdlLOC
        // bridge works -- the first version kept the position only in a C# object and a retry
        // would have re-seeked to stale bytes.
        bool seekWritten = false;
        for (int i = 0; i < 4; i++)
        {
            if (PsxRam.ReadU8(WorkspaceAddress + SoundState.SeekPosition + i) != 0)
            {
                seekWritten = true;
            }
        }

        Check(seekWritten, "la position de seek est ecrite DANS le workspace, pas seulement en C#");

        // ---- state 7 is where the port stops, asserted rather than hoped. SsVabTransCompleted is
        // `return default` in LibSnd, so state 7's "not yet" test is true for ever. When someone
        // implements libsnd, THIS assertion is the one that fails, and that is the signal to update
        // it -- it marks the remaining blocker rather than wishing it away.
        Check(FUN(0, 7) == 7, "etat 7 se tient: SsVabTransCompleted est un stub qui rend 0");

        // ---- 8 is terminal and sticky: it fails the unsigned range test at the top.
        Check(FUN(0, 8) == 8, "etat 8 est terminal et se rend lui-meme");
        Check(FUN(0, 9) == 9, "hors plage, l'etat est renvoye inchange");

        Console.WriteLine(s_failures == 0
            ? "SOUND-LOADER: toutes les verifications passent"
            : $"SOUND-LOADER: {s_failures} echec(s)");
        return s_failures == 0 ? 0 : 1;
    }

    private static int FUN(int id, int state) => SoundDriver.SoundCdLoadStep(id, state);

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            s_failures++;
            Console.WriteLine($"  ECHEC: {label}");
        }
    }
}
