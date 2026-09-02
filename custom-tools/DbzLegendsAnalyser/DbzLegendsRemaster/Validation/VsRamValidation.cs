using System;
using DbzLegendsRemaster.VS_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: guards the seam that was missing while the whole VS.EXE port was written.
//
// PsxRam holds ONE installed address resolver, swapped per overlay by PsxSdkBridges. There was no
// VS.EXE entry in that bridge: the previous overlay's resolver stayed installed, every VS.EXE
// address matched nothing, every read returned zero and every write was dropped — in silence.
// Three tranches and ten thousand lines were correct and inert, and the build was green throughout.
//
// Nothing existing would have caught it. A compiler cannot see it, and the eight benches all
// exercise TITLE.EXE and SELECT.EXE, whose resolvers were fine. So this bench asks the only
// question that distinguishes a live port from a dead one: does a write to a VS.EXE address come
// back when it is read?
internal static class VsRamValidation
{
    private static int _failures;

    internal static int Run()
    {
        PsxSdkBridges.Install();
        PsxSdkBridges.ActivateVsExe();

        Console.WriteLine("=== resolveur d'adresses VS.EXE ===");

        // Touching AnimVm forces its static constructor, which is what registers the 0x8C48
        // workspace region. The same is true of the other spans below: they are declared by field
        // initialisers, so each is reached through the class that owns it.
        Console.WriteLine($"  workspace VM  0x{AnimVm.g_animSharedVarTable:X8}  "
            + $"region {AnimVm.RAM_801f2000.Length} octets");

        // The battle context and the fighters live in the PSYQ heap the overlay arms, so they are
        // not checked here: main has to run for them to exist. What follows are the spans that
        // exist from static initialisation, one per file that declares a region.
        //
        // THE ORDERING TABLE AT 0x800B0F28 IS DELIBERATELY NOT CHECKED, and the first version of
        // this bench got that wrong. It reported a failure there, and the bench was what was wrong,
        // not the resolver: VS_EXE_exe declares that region from DeclareOrderingTableAddress(),
        // which main calls just before entering the frame loop — not from a field initialiser. So
        // before main runs it genuinely does not resolve, and that is the port following the
        // original, where nothing exists until main sets it up. Checking it here would have meant
        // calling the declaration by hand purely to satisfy the check, which proves nothing.
        Check("workspace de la VM", AnimVm.g_animSharedVarTable, 0x5678);
        Check("table de variables VM", AnimVm.g_animSharedVarTable + 0x10, 0x9abc);
        Check("compteurs de maillage", AnimVm.g_meshCountBuffer, 0x0f0f);
        Check("pointeurs de flux", AnimVm.g_meshStreamPtrBuffer, 0x4321);
        Check("bloc combattants", unchecked((int)0x8008DA48), 0xbeef);
        Check("RAM haute partagee", unchecked((int)0x801FF102), 0x0025);

        // The negative control. Without one, a resolver that answered YES to everything — say by
        // handing back one giant array — would pass every line above and still be wrong. 0x00000000
        // is not modelled by any overlay.
        int probe = 0;
        bool wrote = PsxRam.WriteBytes(probe, new byte[] { 0xff, 0xff });
        if (wrote)
        {
            Console.WriteLine("  ECHEC: 0x00000000 se resout, or aucun overlay ne le modelise");
            _failures++;
        }
        else
        {
            Console.WriteLine("  temoin negatif : 0x00000000 ne se resout pas, comme attendu");
        }

        Console.WriteLine(_failures == 0
            ? "=== les adresses VS.EXE se resolvent et retiennent ce qu'on y ecrit"
            : $"=== {_failures} echec(s)");
        return _failures == 0 ? 0 : 1;
    }

    // JUSTIFICATION: backend MonoGame only
    private static void Check(string label, int address, ushort value)
    {
        PsxRam.WriteU16(address, value);
        ushort read = PsxRam.ReadU16(address);
        if (read == value)
        {
            Console.WriteLine($"  {label,-24} 0x{address:X8}  ecrit 0x{value:X4}, relu 0x{read:X4}");
            return;
        }

        Console.WriteLine($"  {label,-24} 0x{address:X8}  ECHEC: ecrit 0x{value:X4}, relu 0x{read:X4}");
        _failures++;
    }
}
