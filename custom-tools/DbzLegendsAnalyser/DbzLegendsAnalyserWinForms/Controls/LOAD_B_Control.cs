using PsxTools2;

namespace DbzLegendsAnalyserWinForms.Controls;

public partial class LOAD_B_Control : UserControl
{
    private readonly Dictionary<string, Bitmap> _extractedImages = new();

    public LOAD_B_Control()
    {
        InitializeComponent();
    }

    public record ImageExtractionParameter(int Offset, int Length, int Width, int Height, PsxImageLoader.PsxPixelMode PixelMode, int NumberOfPalette, PsxImageLoader.PsxClut PsxClut)
    {
        public override string ToString()
        {
            return $"{{ Offset = {Offset}, Length = {Length}, Width = {Width}, Height = {Height}, PixelMode = {PixelMode}, NumberOfPalette = {NumberOfPalette} }}";
        }
    }

    public void Initialize(string gamePath)
    {
        var filePath = Path.Combine(gamePath, "CHR_DATA", "LOAD.B");
        var file = File.ReadAllBytes(filePath);

        // LOAD.B structure: multiple sections of 20480 bytes (10 CD sectors)
        // Each section:
        //   - 0x000-0x1FF (512 bytes): CLUT palette (256 colors * 2 bytes)
        //   - 0x200-end: LZSS compressed image data
        const int SectionSize = 20480; // 10 * 2048 bytes
        const int PaletteOffset = 0x000;
        const int PaletteSize = 0x200; // 512 bytes = 256 colors
        const int CompressedDataOffset = 0x200;

        // Calculate number of sections, including partial last section
        int numSections = (file.Length + SectionSize - 1) / SectionSize;

        _extractedImages.Clear();

        for (int sectionIndex = 0; sectionIndex < numSections; sectionIndex++)
        {
            int sectionStart = sectionIndex * SectionSize;
            
            // Extract palette (256 colors total, 16-bit each)
            // Trying 8bpp mode with 256 color palette
            var paletteColors = BinaryReaderHelper.ReadUShortArrayFast(
                file, 
                sectionStart + PaletteOffset, 
                256
            );
            var clut256 = new PsxImageLoader.PsxClut(paletteColors, ColorsPerPalette: 256);

            // Extract and decompress image data
            // Note: compressed data can span beyond current section (e.g., section 8 uses 20258 bytes)
            // So we allow reading from compressed start to end of file
            int compressedDataStart = sectionStart + CompressedDataOffset;
            int availableBytes = file.Length - compressedDataStart;
            
            if (availableBytes <= 2)
            {
                continue; // Not enough data for LZSS header
            }
            
            // Allow decompressor to read all remaining file data (not just current section)
            var compressedData = new byte[availableBytes];
            Array.Copy(file, compressedDataStart, compressedData, 0, availableBytes);

            byte[] decompressedImage;
            try
            {
                decompressedImage = LzssDecompressor.Decompress(compressedData);
            }
            catch (Exception ex)
            {
                // Skip invalid sections
                continue;
            }

            // Decode image as 160x240 (VRAM units), 8bpp
            // 76800 bytes in 8bpp (1 byte/pixel) = 76800 pixels = 320×240
            // In VRAM units for 8bpp: 320 pixels ÷ 2 = 160 VRAM units
            const int ImageWidth = 160;    // 160 VRAM units (320 pixels)
            const int ImageHeight = 240;   // 240 pixels

            try
            {
                var bitmap = PsxImageLoader.DecodeToBitmap(
                    decompressedImage,
                    new PsxImageLoader.PsxImageLayout(ImageWidth, ImageHeight),
                    new PsxImageLoader.PsxImageFormat(PsxImageLoader.PsxPixelMode.Bpp8),
                    clut256,
                    paletteIndex: 0
                );

                _extractedImages.Add($"Section_{sectionIndex}", bitmap);
            }
            catch (Exception ex)
            {
                // Skip if decode fails
                continue;
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
