using System;
using System.IO;
using DbzLegendsRemaster.MOVIE_EXE;
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