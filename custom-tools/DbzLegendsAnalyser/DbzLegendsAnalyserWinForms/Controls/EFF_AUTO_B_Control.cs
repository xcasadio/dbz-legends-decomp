using PsxTools2;

namespace DbzLegendsAnalyserWinForms.Controls;

public partial class EFF_AUTO_B_Control : AnalyserControl
{
    private readonly Dictionary<string, Bitmap> _extractedImages = new();

    public EFF_AUTO_B_Control()
    {
        InitializeComponent();
    }

    public override void Initialize(string fileName)
    {
        _extractedImages.Clear();
        
        var file = File.ReadAllBytes(fileName);

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
            int remainingSize = file.Length - Image1Offset;
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

        // Image 2: after image 1 (estimate position)
        try
        {
            const int Image2Offset = 0x355C;
            var buffer = file.AsSpan(Image2Offset).ToArray();
            var decompressed2 = LzssDecompressor.Decompress(buffer);
            
            for (int i = 0; i < 5; i++)
            {
                var bitmap2 = PsxImageLoader.DecodeToBitmap(
                    decompressed2,
                    new PsxImageLoader.PsxImageLayout(ImageWidth, ImageHeight),
                    new PsxImageLoader.PsxImageFormat(PsxImageLoader.PsxPixelMode.Bpp4),
                    palettes[i],
                    paletteIndex: 0
                );
                
                _extractedImages.Add($"Image_2_AutoEffect_{i}", bitmap2);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image 2: Failed to decode - {ex.Message}");
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
