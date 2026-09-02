using System;
using System.IO;
using DbzLegendsRemaster.MOVIE_EXE;
using DbzLegendsRemaster.SELECT_EXE;
using DbzLegendsRemaster.SLPS_003_55;
using DbzLegendsRemaster.TITLE_EXE;
using PsxSdkMonogame;

namespace DbzLegendsRemaster;

internal static class PsxSdkBridges
{
    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: installs the game-specific PSX RAM and ISO-file resolvers consumed by the shared SDK.
    internal static void Install()
    {
        // The stopwatch is beforefieldinit, so without this it would only start on the first
        // TraceOverlay call and report t=0 for it. Restarting here anchors it to startup.
        s_diagClock.Restart();

        PsxRam.AddressResolver = SLPS_003_55_exe.ResolveAddress;

        string discRoot = Path.Combine(AppContext.BaseDirectory, "data");
        LibDs.DiscFileResolver = isoPath =>
        {
            if (string.IsNullOrEmpty(isoPath))
            {
                return null;
            }

            // LoadExec spells its argument "cdrom:\NAME.EXE;1"; every other call site omits the
            // device prefix. Both resolve to the same file.
            if (isoPath.StartsWith("cdrom:", StringComparison.OrdinalIgnoreCase))
            {
                isoPath = isoPath.Substring("cdrom:".Length);
            }

            int versionSeparator = isoPath.IndexOf(';');
            string relative = versionSeparator >= 0 ? isoPath[..versionSeparator] : isoPath;
            relative = relative.Replace('\\', Path.DirectorySeparatorChar)
                               .TrimStart(Path.DirectorySeparatorChar);
            string candidate = Path.Combine(discRoot, relative);
            return File.Exists(candidate) ? candidate : null;
        };
    }

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: LoadExec replaces the resident executable and its overlapping RAM ranges.
    internal static void ActivateMovieExe()
    {
        PsxRam.AddressResolver = MOVIE_EXE_exe.ResolveAddress;
        TraceOverlay("MOVIE.EXE");
    }

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: LoadExec replaces the resident executable and its overlapping RAM ranges.
    internal static void ActivateTitleExe()
    {
        PsxRam.AddressResolver = TITLE_EXE_exe.ResolveAddress;
        TraceOverlay("TITLE.EXE");
    }

    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: LoadExec replaces the resident executable and its overlapping RAM ranges.
    //
    // The ranges SELECT.EXE's resolver answers for, and how each extent was closed:
    //   0x80080000  40320 B  g_UsagiChunk18DecodedTiles — Ghidra types the symbol ushort[20160],
    //                        which is 35 tiles of 12 x 48 words; the span also stops well below
    //                        the next used address, 0x80090000;
    //   0x80090000  0x20000  the decode scratch — the largest chunk it holds is 160 x 240 VRAM
    //                        halfwords = 76800 B, and the upper bound is the buffer below;
    //   0x800B0000  0x50000  the raw USAGI.B buffer — the live read is 147 sectors = 301056 B, and
    //                        the upper bound is 0x80100000, the next address any SELECT.EXE code
    //                        uses (the BGM.B VAB body, menu state 3);
    //   0x80058E08  112 B    the flat store behind FUN_80030698's triangular table — 28 words, the
    //                        loop's own count, and it ends exactly on libgs's DAT_80058e78;
    //   0x800593B8  84 B     that table's 7 records of 12 bytes, both from the loop's own bounds;
    //   0x801FF000  0x248    the cross-overlay block, through the existing SharedHighRam. SELECT
    //                        touches 0x801FF000..0x801FF247 and nothing else inside it — the same
    //                        extent SharedHighRam already models, including the button-remap
    //                        tables at index 0x10 / 0x1E and the memory-card result word at
    //                        0x801FF068. It is REUSED, not extended. The one SELECT address that
    //                        falls outside it, 0x801FFF00, is the LoadExec header scratch, whose
    //                        address is passed but whose contents are never read;
    //   the SNMAIN heap       0x0078E75C bytes at 0x800692A0, armed by start and never used.
    // The heap span covers every buffer above it, exactly as it does on the console, so
    // PsxHeap.Resolve is chained LAST in SELECT_EXE_exe.ResolveAddress.
    internal static void ActivateSelectExe()
    {
        PsxRam.AddressResolver = SELECT_EXE_exe.ResolveAddress;
        TraceOverlay("SELECT.EXE");
    }

    // VS.EXE — the versus battle overlay, reached by LoadExec from SELECT.EXE's mode menu.
    //
    // This line was MISSING while the whole VS_EXE port was written. PsxRam holds one resolver and
    // this bridge is what installs it, so with no VS.EXE entry the previous overlay's resolver
    // stayed in place: every VS.EXE address matched nothing, every read returned zero, every write
    // was dropped, and none of it raised anything. Three tranches and ten thousand lines were
    // correct and inert. Three separate files had already recorded the hole in their own comments —
    // AnimVm.cs, AnimCmdMesh.cs and FileIo.cs — each assuming another slice owned the fix; the
    // slice that finally reported it up could not close it either, because closing it means editing
    // this file and VS_EXE_exe.cs, and neither was its to touch.
    //
    // Worth keeping as a note about the method rather than only about the bug: a hole that every
    // slice can see and no slice owns is exactly what the ownership rule produces if nobody is
    // holding the seam. That is the main session's job.
    internal static void ActivateVsExe()
    {
        PsxRam.AddressResolver = VS_EXE.VS_EXE_exe.ResolveAddress;
        TraceOverlay("VS.EXE");
    }

    // JUSTIFICATION: backend MonoGame only
    // RELATION: makes the overlay switch observable for acceptance, opt-in through
    // DBZ_OVERLAY_DIAG=1, mirroring the SDK's PE_AUDIO_DIAG pattern. No runtime control flow
    // depends on it.
    private static readonly System.Diagnostics.Stopwatch s_diagClock =
        System.Diagnostics.Stopwatch.StartNew();

    private static void TraceOverlay(string overlayName)
    {
        if (Environment.GetEnvironmentVariable("DBZ_OVERLAY_DIAG") == "1")
        {
            Console.WriteLine($"[overlay] t={s_diagClock.ElapsedMilliseconds}ms LoadExec -> {overlayName}");
        }
    }
}