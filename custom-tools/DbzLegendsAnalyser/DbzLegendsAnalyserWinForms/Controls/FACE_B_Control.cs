using PsxTools2;

namespace DbzLegendsAnalyserWinForms.Controls;

public partial class FACE_B_Control : AnalyserControl
{
    private readonly Dictionary<string, Bitmap> _extractedImages = new();

    public FACE_B_Control()
    {
        InitializeComponent();
    }

    public override void Initialize(string fileName)
    {
        var file = File.ReadAllBytes(fileName);

        // Each section:
        //clutOffset = entryOffset + 0x000 (0x20 bytes)
        //img0Offset = entryOffset + 0x020(0x480 bytes)
        //img1Offset = entryOffset + 0x4A0
        //img2Offset = entryOffset + 0x920
        const int SectionSize = 0x1000; // 38 * 0x1000 = 0x26000
        const int PaletteOffset = 0x000;
        const int PaletteSize = 0x20;

        int numSections = (file.Length + SectionSize - 1) / SectionSize;

        _extractedImages.Clear();

        for (int sectionIndex = 0; sectionIndex < numSections; sectionIndex++)
        {
            int sectionStart = sectionIndex * SectionSize;
            
            // Extract palette (16 colors, 16-bit each)
            var paletteColors = BinaryReaderHelper.ReadUShortArrayFast(
                file, 
                sectionStart + PaletteOffset, 
                16
            );
            var clut16 = new PsxImageLoader.PsxClut(paletteColors, ColorsPerPalette: 16);

            // Each section contains 3 images of 12×48 pixels (4bpp)
            // Image dimensions: 0xc × 0x30 VRAM units = 12 × 48 pixels
            // Size per image: 12 × 48 ÷ 2 = 1152 bytes (4bpp = 0.5 byte/pixel)
            const int ImageWidth = 0xc;   // 12 VRAM units (48 pixels in 4bpp)
            const int ImageHeight = 0x30; // 48 pixels
            const int ImageSize = 0x480;  // 1152 bytes

            // Extract and decode all 3 images from this section
            int[] imageOffsets = { 0x020, 0x4A0, 0x920 };
            
            for (int imgIndex = 0; imgIndex < 3; imgIndex++)
            {
                int imageOffset = sectionStart + imageOffsets[imgIndex];
                
                if (imageOffset + ImageSize > file.Length)
                    continue;
                
                var imageBuffer = new byte[ImageSize];
                Array.Copy(file, imageOffset, imageBuffer, 0, ImageSize);

                try
                {
                    var bitmap = PsxImageLoader.DecodeToBitmap(
                        imageBuffer,
                        new PsxImageLoader.PsxImageLayout(ImageWidth, ImageHeight),
                        new PsxImageLoader.PsxImageFormat(PsxImageLoader.PsxPixelMode.Bpp4),
                        clut16,
                        paletteIndex: 0
                    );

                    _extractedImages.Add($"Section_{sectionIndex}_Image_{imgIndex}", bitmap);
                }
                catch (Exception ex)
                {
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
