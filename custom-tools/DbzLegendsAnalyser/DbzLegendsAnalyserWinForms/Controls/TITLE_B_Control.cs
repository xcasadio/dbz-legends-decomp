using PsxTools2;

namespace DbzLegendsAnalyserWinForms.Controls;

public partial class TITLE_B_Control : AnalyserControl
{
    private readonly Dictionary<string, Bitmap> _extractedImages = new();

    public TITLE_B_Control()
    {
        InitializeComponent();
    }

    public override void Initialize(string fileName)
    {
        _extractedImages.Clear();
        
        var file = File.ReadAllBytes(fileName);

        /*
        - Entry 0 (image)
            dataOffset=0x5D8, vram=(384,0), widthWords=0xA0, h=0xF0, isClut=0
            8bpp (car palette CLUT256 juste après)
            Dimensions pixels : 320×240
        - Entry 1 (palette)
            dataOffset=0x1B8, vram=(384,240), widthWords=0x100, h=1, isClut=1
            CLUT256 (512 bytes)
        - Entry 2 (image)
            dataOffset=0x131D8, vram=(384,256), widthWords=0x80, h=0x100, isClut=0
            8bpp
            Dimensions pixels : 256×256
        - Entry 3 (palette)
            dataOffset=0x3B8, vram=(384,241), widthWords=0x100, h=1, isClut=1
            CLUT256 (512 bytes) (non compressée : elle commence par 0x0000 = couleur 0)
        - Entry 4 (image)
            dataOffset=0x231D8, vram=(704,256), widthWords=0x40, h=0x80, isClut=0
            4bpp, compressée via FUN_80034e34 (décompresse pile en 0x4000 bytes)
            Dimensions pixels : 256×128
        - Entry 5 (palette)
            dataOffset=0x5B8, vram=(704,384), widthWords=0x10, h=1, isClut=1
            CLUT16 (32 bytes)
         */

        // Extract palette (80 colors)
        const int PaletteOffset = 0x00;
        const int PaletteColorCount = 80;
        var paletteColors = BinaryReaderHelper.ReadUShortArrayFast(file, PaletteOffset, PaletteColorCount);
        
        // Create 16-color palette from first 16 colors
        var palettes = new PsxImageLoader.PsxClut[5];

        for (int i = 0; i < 5; i++)
        {
            var palette16Colors = new ushort[16];
            Array.Copy(paletteColors, i * 16, palette16Colors, 0, 16);
            palettes[i] = new PsxImageLoader.PsxClut(palette16Colors, ColorsPerPalette: 16);
        }

        // Image 1: at offset 0xA0
        const int Image1Offset = 0xA0;
        const int ImageWidth = 0x40;  // VRAM units (256 pixels for 4bpp)
        const int ImageHeight = 0x100; // pixels (256)
        
        try
        {
            var compressed1 = file.AsSpan(Image1Offset).ToArray();
            var decompressed1 = LzssDecompressor.Decompress(compressed1);

            for (int i = 0; i < 5; i++)
            {
                var bitmap1 = PsxImageLoader.DecodeToBitmap(
                    decompressed1,
                    new PsxImageLoader.PsxImageLayout(ImageWidth, ImageHeight),
                    new PsxImageLoader.PsxImageFormat(PsxImageLoader.PsxPixelMode.Bpp4),
                    palettes[i],
                    paletteIndex: 0
                );

                _extractedImages.Add($"Image_1_AutoEffect_{i}", bitmap1);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image 1: Failed to decode - {ex.Message}");
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
