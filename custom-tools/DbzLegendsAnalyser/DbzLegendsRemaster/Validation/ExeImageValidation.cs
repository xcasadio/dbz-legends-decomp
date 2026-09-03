using System;
using System.IO;
using PsxSdkMonogame;

namespace DbzLegendsRemaster.Validation;

// JUSTIFICATION: backend MonoGame only
// RELATION: guards the image-backed data mechanism — the last link of every overlay's address
// resolver, which hands back the bytes of the executable where nothing else claims an address.
//
// Nothing existing could see the hole this closes. Every other bench reads addresses the port
// declares, and declared addresses were never the problem; the problem was every table the game
// reads out of its own image, which read zero through a green build and ten green benches. So
// this bench asks the questions that distinguish a live mechanism from a dead one, and each
// expected value is read out of data/*.EXE by the bench itself rather than typed in — a typed
// constant would drift with the file, and a bench that agrees with a stale constant proves the
// wrong thing.
internal static class ExeImageValidation
{
    private static int _failures;

    internal static int Run()
    {
        PsxSdkBridges.Install();

        string dataRoot = Path.Combine(AppContext.BaseDirectory, "data");
        byte[] vsFile = File.ReadAllBytes(Path.Combine(dataRoot, "VS.EXE"));
        byte[] selectFile = File.ReadAllBytes(Path.Combine(dataRoot, "SELECT.EXE"));
        byte[] endingFile = File.ReadAllBytes(Path.Combine(dataRoot, "ENDING.EXE"));

        Console.WriteLine("=== l'image de l'executable comme derniere carte d'adresses ===");

        // ------------------------------------------------------------------ VS.EXE
        PsxSdkBridges.ActivateVsExe();

        int vsLoad = BitConverter.ToInt32(vsFile, 0x18);
        int vsSize = BitConverter.ToInt32(vsFile, 0x1C);
        Expect("en-tete VS.EXE lu, pas suppose",
            PsxExeImage.LoadAddress == vsLoad && PsxExeImage.BodySize == vsSize,
            $"charge 0x{PsxExeImage.LoadAddress:X8} corps 0x{PsxExeImage.BodySize:X}, "
            + $"fichier 0x{vsLoad:X8} / 0x{vsSize:X}");

        // The CH_BIN filename table — a .rodata string the battle scene indexes, and which read
        // zero before. Sixteen bytes compared against the file at the same address.
        CheckAgainstFile("table CH_BIN 0x80081A50", vsFile, vsLoad, unchecked((int)0x80081A50), 16);

        // The per-character short table: entry 1 is 500 in the file.
        CheckAgainstFile("table de shorts 0x80082264", vsFile, vsLoad, unchecked((int)0x80082264), 8);

        // THE BUFFER HANDED BACK IS THE IMAGE COPY, not a region that happens to hold the same
        // values. This is the line that would catch a RamRegion silently re-added over the span.
        var unclaimed = PsxRam.AddressResolver(unchecked((int)0x80081A50));
        Expect("0x80081A50 est servi par l'image elle-meme",
            unclaimed.HasValue && PsxExeImage.IsImageBuffer(unclaimed.Value.buffer),
            unclaimed.HasValue ? "resolu, mais par un autre tampon" : "non resolu");

        // A DECLARED REGION STILL WINS. Roster.cs declares 0x80083CF0 with the image's own bytes
        // transcribed by hand; the resolver must hand back THAT buffer, not the image's, or the
        // roster builder and the battle manager would be writing into two different records.
        //
        // THE FIRST RUN OF THIS LINE FAILED, AND THE FAILURE WAS INFORMATION. The region is a
        // static field initialiser, so it does not exist until something touches the Roster
        // class, and until then the image answered for the address. That is the order main
        // follows — it calls Roster.FUN_8005cbe0 before anything reads the record — so the bench
        // follows it too, the way VsRamValidation touches AnimVm. But it is a property of this
        // mechanism worth knowing: a lazily-declared region is invisible until its class runs, and
        // the image now fills that window where before it was simply unresolved.
        _ = VS_EXE.Roster.PTR_DAT_800844b8;
        var claimed = PsxRam.AddressResolver(unchecked((int)0x80083CF0));
        Expect("0x80083CF0 reste servi par la region declaree de Roster",
            claimed.HasValue && !PsxExeImage.IsImageBuffer(claimed.Value.buffer),
            claimed.HasValue ? "resolu par l'image: la region a ete court-circuitee" : "non resolu");
        CheckAgainstFile("  ... et ses octets sont ceux du fichier", vsFile, vsLoad,
            unchecked((int)0x80083CF0), 16);

        // MUTABLE, AND RE-ARMED. 0x80082164 carries a lock bit the game raises with ori 0x80 and
        // clears with andi 0x7F. A write must land; a fresh Activate must undo it.
        int lockAddress = unchecked((int)0x80082164);
        byte fileByte = vsFile[0x800 + (lockAddress - vsLoad)];
        PsxRam.WriteBytes(lockAddress, new[] { (byte)(fileByte | 0x80) });
        byte afterWrite = PsxRam.ReadBytes(lockAddress, 1)[0];
        Expect("l'image est une copie MUTABLE", afterWrite == (byte)(fileByte | 0x80),
            $"ecrit 0x{fileByte | 0x80:X2}, relu 0x{afterWrite:X2}");

        PsxSdkBridges.ActivateVsExe();
        byte afterRearm = PsxRam.ReadBytes(lockAddress, 1)[0];
        Expect("un nouvel Activate rearme une copie FRAICHE", afterRearm == fileByte,
            $"fichier 0x{fileByte:X2}, relu apres rearmement 0x{afterRearm:X2}");

        // NEGATIVE CONTROLS. One byte past the extent, and the null page.
        Expect("0x80105800 (un octet apres la fin) ne se resout pas",
            !PsxRam.AddressResolver(vsLoad + vsSize).HasValue, "resolu");
        Expect("0x00000000 ne se resout pas",
            !PsxRam.AddressResolver(0).HasValue, "resolu");

        // ------------------------------------------------------------------ SELECT.EXE
        // The options screen's row-1 box tables at 0x80055A50, which SELECT_EXE_exe carries as a
        // C# literal but which the original reads by address. Through PsxRam it now reads the
        // file, so the literal and the image can be held to each other.
        PsxSdkBridges.ActivateSelectExe();
        int selLoad = BitConverter.ToInt32(selectFile, 0x18);
        CheckAgainstFile("SELECT.EXE 0x80055A50 (tables d'options)", selectFile, selLoad,
            unchecked((int)0x80055A50), 24);
        Expect("l'image VS.EXE n'est plus armee apres ActivateSelectExe",
            PsxExeImage.BodySize == BitConverter.ToInt32(selectFile, 0x1C),
            $"corps arme 0x{PsxExeImage.BodySize:X}");

        // ------------------------------------------------------------------ ENDING.EXE
        // The one image on the disc that does NOT load at 0x80020000. Parsed directly: the
        // mechanism must take the address from the header or it is wrong for this file.
        PsxExeImage.Arm(Path.Combine(dataRoot, "ENDING.EXE"));
        int endLoad = BitConverter.ToInt32(endingFile, 0x18);
        Expect("ENDING.EXE se charge a l'adresse de SON en-tete",
            PsxExeImage.LoadAddress == endLoad && endLoad != vsLoad,
            $"en-tete 0x{endLoad:X8}, arme 0x{PsxExeImage.LoadAddress:X8}");
        Expect("  ... et 0x80020000 tombe DANS son etendue, pas a son debut",
            PsxExeImage.Resolve(vsLoad).HasValue && PsxExeImage.Resolve(vsLoad).Value.offset == vsLoad - endLoad,
            "0x80020000 non resolu ou a un decalage inattendu");

        // Leave the bridge the way a caller expects to find it.
        PsxSdkBridges.ActivateSelectExe();

        Console.WriteLine(_failures == 0
            ? "=== l'image repond en dernier, en copie mutable, a l'adresse de son en-tete"
            : $"=== {_failures} echec(s)");
        return _failures == 0 ? 0 : 1;
    }

    // JUSTIFICATION: backend MonoGame only
    private static void CheckAgainstFile(string label, byte[] file, int loadAddress, int address, int count)
    {
        byte[] read = PsxRam.ReadBytes(address, count);
        int fileOffset = 0x800 + (address - loadAddress);
        bool same = read != null && read.Length == count;
        for (int i = 0; same && i < count; i++)
        {
            same = read[i] == file[fileOffset + i];
        }

        string shown = read == null ? "(null)" : BitConverter.ToString(read, 0, Math.Min(count, 8)).Replace("-", " ");
        string expected = BitConverter.ToString(file, fileOffset, Math.Min(count, 8)).Replace("-", " ");
        Expect(label, same, $"lu {shown}, fichier {expected}");
    }

    // JUSTIFICATION: backend MonoGame only
    private static void Expect(string label, bool ok, string detail)
    {
        if (ok)
        {
            Console.WriteLine($"  {label,-58} OK");
            return;
        }

        Console.WriteLine($"  {label,-58} ECHEC: {detail}");
        _failures++;
    }
}
