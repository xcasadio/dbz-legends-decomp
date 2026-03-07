using System;
using System.IO;
using PsxTools2;

// Add reference to PsxTools2
string basePath = @"d:\development\repo\dbz-legends-decomp\data\STG";
string[] files = { "STG1MD.B", "STG2MD.B" };

foreach (var filename in files)
{
    string filePath = Path.Combine(basePath, filename);
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"File not found: {filePath}");
        continue;
    }

    Console.WriteLine($"\n=== Analyzing {filename} ===");
    Console.WriteLine($"File size: {new FileInfo(filePath).Length} bytes\n");

    try
    {
        var stgModel = StgMdLoader.LoadStgMdFile(filePath);
        
        Console.WriteLine($"Mesh count: {stgModel.MeshTable.Length}");
        Console.WriteLine();

        for (int i = 0; i < stgModel.MeshTable.Length; i++)
        {
            var entry = stgModel.MeshTable[i];
            var mesh = entry.MeshData;
            
            if (mesh == null)
            {
                Console.WriteLine($"Mesh #{i}: No data loaded");
                continue;
            }
            
            Console.WriteLine($"Mesh #{i}:");
            Console.WriteLine($"  Value1: 0x{entry.Value1:X8}");
            Console.WriteLine($"  File offset: 0x{entry.MeshDataOffset:X}");
            Console.WriteLine($"  Indirect offset: 0x{mesh.IndirectOffset:X}");
            Console.WriteLine($"  Table part count: {mesh.TablePartCount}");
            Console.WriteLine($"  Found parts: {mesh.Parts.Count}");
            
            int totalPrimitives = 0;
            int totalBytes = 0;
            foreach (var part in mesh.Parts)
            {
                totalPrimitives += part.PrimitiveCount;
                totalBytes += part.EstimatedSize;
            }
            Console.WriteLine($"  Total primitives: {totalPrimitives}");
            Console.WriteLine($"  Total bytes: {totalBytes}");
            
            if (mesh.Parts.Count > 0)
            {
                Console.WriteLine("  Parts:");
                for (int j = 0; j < mesh.Parts.Count; j++)
                {
                    var part = mesh.Parts[j];
                    Console.WriteLine($"    Part {j}: {part.PrimitiveType} × {part.PrimitiveCount} = {part.EstimatedSize} bytes @ 0x{part.FileOffset:X}");
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
}

Console.WriteLine("\nAnalysis complete!");
