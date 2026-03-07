using PsxTools2;
using System;

// Analyzer for STGxMD.B files using new loader
Console.WriteLine("=== STGxMD.B Analysis ===");
Console.WriteLine();

var files = new[]
{
    @"D:\development\repo\dbz-legends-decomp\data\STG\STG1MD.B",
    @"D:\development\repo\dbz-legends-decomp\data\STG\STG2MD.B"
};

foreach (var filePath in files)
{
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"File not found: {filePath}");
        continue;
    }

    Console.WriteLine($"=== {Path.GetFileName(filePath)} ===");
    var fileBytes = File.ReadAllBytes(filePath);
    Console.WriteLine($"File size: {fileBytes.Length} bytes (0x{fileBytes.Length:X})");
    Console.WriteLine();

    try
    {
        var model = StgMdLoader.LoadStgMdFile(fileBytes);
        
        Console.WriteLine($"Mesh count: {model.MeshTable.Length}");
        Console.WriteLine();

        for (int i = 0; i < model.MeshTable.Length; i++)
        {
            var entry = model.MeshTable[i];
            if (entry.MeshDataOffset == 0) continue;

            Console.WriteLine($"Mesh #{i}:");
            Console.WriteLine($"  Value1: 0x{entry.Value1:X8}");
            Console.WriteLine($"  Offset: 0x{entry.MeshDataOffset:X8}");
            
            if (entry.MeshData != null)
            {
                var mesh = entry.MeshData;
                Console.WriteLine($"  Indirect offset: 0x{mesh.IndirectOffset:X}");
                Console.WriteLine($"  Table part count: {mesh.TablePartCount}");
                Console.WriteLine($"  Parts found: {mesh.Parts.Count}");
                Console.WriteLine($"  Total primitives: {mesh.TotalPrimitives}");
                Console.WriteLine($"  Total bytes: {mesh.TotalBytes}");
                
                foreach (var part in mesh.Parts)
                {
                    Console.WriteLine($"    - {part.Description} @ 0x{part.FileOffset:X}");
                }
            }
            
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
    
    Console.WriteLine();
    Console.WriteLine("-------------------------------------------");
    Console.WriteLine();
}
