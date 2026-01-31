using PsxTools2;

namespace DbzLegendsAnalyserWinForms.Controls;

public partial class STG_TX_Control : AnalyserControl
{
    private readonly Dictionary<string, Bitmap> _extractedImages = new();

    public STG_TX_Control()
    {
        InitializeComponent();
    }

    public override void Initialize(string fileName)
    {
        var file = File.ReadAllBytes(fileName);

        // STGxTX.B structure:
        // - uint textureCount (4 bytes)
        // - TextureEntry[textureCount] (28 bytes each = 7 uints)
        //   - uint compressionType (0 = LZSS, 1 = raw)
        //   - uint dataOffset
        //   - uint vramX
        //   - uint vramY
        //   - uint width (VRAM units)
        //   - uint height (pixels)
        //   - uint isClut (0 = image, 1 = palette)
        // - byte[] textureData

        if (file.Length < 4)
            return;

        uint textureCount = BitConverter.ToUInt32(file, 0);
        int textureDataBaseOffset = 0; 

        _extractedImages.Clear();

        var palettes = new Dictionary<int, PsxImageLoader.PsxClut>();
        
        for (int i = 0; i < textureCount; i++)
        {
            int entryOffset = 4 + i * 28;
            uint compressionType = BitConverter.ToUInt32(file, entryOffset + 0);
            uint dataOffset = BitConverter.ToUInt32(file, entryOffset + 4);
            uint vramX = BitConverter.ToUInt32(file, entryOffset + 8);
            uint vramY = BitConverter.ToUInt32(file, entryOffset + 12);
            uint width = BitConverter.ToUInt32(file, entryOffset + 16);
            uint height = BitConverter.ToUInt32(file, entryOffset + 20);
            uint isClut = BitConverter.ToUInt32(file, entryOffset + 24);

            if (isClut == 1) // It's a palette
            {
                int absoluteOffset = textureDataBaseOffset + (int)dataOffset;
                int paletteColorCount = (int)width; // For CLUT, width = number of colors
                
                if (absoluteOffset + paletteColorCount * 2 <= file.Length)
                {
                    var paletteColors = BinaryReaderHelper.ReadUShortArrayFast(file, absoluteOffset, paletteColorCount);
                    int colorsPerPalette = paletteColorCount >= 256 ? 256 : 16;
                    palettes[i] = new PsxImageLoader.PsxClut(paletteColors, ColorsPerPalette: colorsPerPalette);
                    System.Diagnostics.Debug.WriteLine($"Palette {i}: {paletteColorCount} colors at VRAM ({vramX},{vramY})");
                }
            }
        }

        // Second pass: Extract and decode images
        for (int i = 0; i < textureCount; i++)
        {
            int entryOffset = 4 + i * 28;
            uint compressionType = BitConverter.ToUInt32(file, entryOffset + 0);
            uint dataOffset = BitConverter.ToUInt32(file, entryOffset + 4);
            uint vramX = BitConverter.ToUInt32(file, entryOffset + 8);
            uint vramY = BitConverter.ToUInt32(file, entryOffset + 12);
            uint width = BitConverter.ToUInt32(file, entryOffset + 16);
            uint height = BitConverter.ToUInt32(file, entryOffset + 20);
            uint isClut = BitConverter.ToUInt32(file, entryOffset + 24);

            if (isClut == 0)
            {
                try
                {
                    int absoluteOffset = textureDataBaseOffset + (int)dataOffset;
                    
                    // Calculate data size by finding next texture's offset
                    int dataSize;
                    if (i + 1 < textureCount)
                    {
                        int nextEntryOffset = 4 + (i + 1) * 28;
                        uint nextDataOffset = BitConverter.ToUInt32(file, nextEntryOffset + 4);
                        dataSize = (int)(nextDataOffset - dataOffset);
                    }
                    else
                    {
                        dataSize = file.Length - absoluteOffset;
                    }
                    
                    byte[] imageData;

                    if (compressionType == 0) //0 = Lzss compression
                    {
                        var compressedData = new byte[dataSize];
                        Array.Copy(file, absoluteOffset, compressedData, 0, dataSize);
                        imageData = LzssDecompressor.Decompress(compressedData);
                    }
                    else // Raw data
                    {
                        imageData = new byte[dataSize];
                        Array.Copy(file, absoluteOffset, imageData, 0, dataSize);
                    }

                    // Find appropriate palette for this texture
                    // Strategy: use the closest previous palette entry
                    PsxImageLoader.PsxClut? palette = null;
                    int paletteIndex = -1;
                    for (int p = i - 1; p >= 0; p--)
                    {
                        if (palettes.TryGetValue(p, out palette))
                        {
                            paletteIndex = p;
                            break;
                        }
                    }
                    
                    // Fallback: use first available palette
                    if (palette == null)
                    {
                        palette = palettes.Values.FirstOrDefault() ?? new PsxImageLoader.PsxClut(new ushort[16], ColorsPerPalette: 16);
                        paletteIndex = palettes.Keys.FirstOrDefault();
                    }
                    
                    PsxImageLoader.PsxPixelMode pixelMode;
                    
                    if (palette.ColorsPerPalette == 256)
                    {
                        pixelMode = PsxImageLoader.PsxPixelMode.Bpp8;
                    }
                    else // 16 colors or other
                    {
                        pixelMode = PsxImageLoader.PsxPixelMode.Bpp4;
                        
                        // Ensure we have a 16-color palette for 4bpp
                        if (palette.ColorsPerPalette != 16)
                        {
                            var originalColors = palette.ColorsBgr555;
                            var colors16 = new ushort[16];
                            Array.Copy(originalColors, 0, colors16, 0, Math.Min(16, originalColors.Length));
                            palette = new PsxImageLoader.PsxClut(colors16, ColorsPerPalette: 16);
                        }
                    }
                    
                    var bitmap = PsxImageLoader.DecodeToBitmap(
                        imageData,
                        new PsxImageLoader.PsxImageLayout((int)width, (int)height),
                        new PsxImageLoader.PsxImageFormat(pixelMode),
                        palette,
                        paletteIndex: 0
                    );

                    _extractedImages.Add($"Texture_{i}_Pal{paletteIndex}_{pixelMode}", bitmap);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Texture {i}: Failed to decode - {ex.Message}");
                    continue;
                }
            }
        }

        listBoxOffsets.SuspendLayout();
        listBoxOffsets.Items.Clear();

        foreach (var key in _extractedImages.Keys)
        {
            listBoxOffsets.Items.Add(key);
        }

        listBoxOffsets.ResumeLayout();
    }

    private void listBoxOffsets_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (listBoxOffsets.SelectedItem == null)
        {
            imageViewerControl1.Image = null;
            return;
        }
        var selectedKey = ((string)listBoxOffsets.SelectedItem);
        imageViewerControl1.Image = _extractedImages.GetValueOrDefault(selectedKey);
    }
}
