using System;
using System.IO;
using DbzLegendsRemaster.SLPS_003_55;
using PsxSdkMonogame;

namespace DbzLegendsRemaster;

internal static class PsxSdkBridges
{
    // JUSTIFICATION: PSX hardware adaptation only
    // RELATION: installs the game-specific PSX RAM and ISO-file resolvers consumed by the shared SDK.
    internal static void Install()
    {
        PsxRam.AddressResolver = SLPS_003_55_exe.ResolveAddress;

        string discRoot = Path.Combine(AppContext.BaseDirectory, "data");
        LibDs.DiscFileResolver = isoPath =>
        {
            if (string.IsNullOrEmpty(isoPath))
            {
                return null;
            }

            int versionSeparator = isoPath.IndexOf(';');
            string relative = versionSeparator >= 0 ? isoPath[..versionSeparator] : isoPath;
            relative = relative.Replace('\\', Path.DirectorySeparatorChar)
                               .TrimStart(Path.DirectorySeparatorChar);
            string candidate = Path.Combine(discRoot, relative);
            return File.Exists(candidate) ? candidate : null;
        };
    }
}