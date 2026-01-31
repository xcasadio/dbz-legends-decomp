using PsxTools2;

namespace DbzLegendsAnalyserWinForms.Controls;

public partial class OV_CHR_A_Control : AnalyserControl
{
    private readonly Dictionary<string, Bitmap> _extractedImages = new();

    public OV_CHR_A_Control()
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

    public override void Initialize(string fileName)
    {
        var file = File.ReadAllBytes(fileName);
        var decomp = LzssDecompressor.Decompress(file);

        if (decomp.Length < 0x7400)
        {
            throw new InvalidDataException($"Decompressed too small: 0x{decomp.Length:X} (expected >= 0x7400)");
        }

        // --- Parse CLUTs ---
        var clut128Colors = BinaryReaderHelper.ReadUShortArrayFast(decomp, 0x0000, 128);
        var clut256Colors = BinaryReaderHelper.ReadUShortArrayFast(decomp, 0x0100, 256);
        var clut128 = new PsxImageLoader.PsxClut(clut128Colors, ColorsPerPalette: 16);
        var clut256 = new PsxImageLoader.PsxClut(clut256Colors, ColorsPerPalette: 256);

        var extractionParameters = new List<ImageExtractionParameter>
        {
            new(0x0300, 0x2000, 0x20, 0x80, PsxImageLoader.PsxPixelMode.Bpp4, 8, clut128),
            new(0x2300, 0x2300, 0x28, 0x70, PsxImageLoader.PsxPixelMode.Bpp4, 8, clut128),
            new(0x4600, 0x1E00, 0x28, 0x60, PsxImageLoader.PsxPixelMode.Bpp8, 1, clut256),
            new(0x6400, 0x1000, 0x40, 0x20, PsxImageLoader.PsxPixelMode.Bpp4, 8, clut128)
        };

        _extractedImages.Clear();
        foreach (var extractionParameter in extractionParameters)
        {
            var img = new byte[extractionParameter.Length];
            Array.Copy(decomp, extractionParameter.Offset, img, 0, img.Length);

            for (int i = 0; i < extractionParameter.NumberOfPalette; i++)
            {
                var bitmap = PsxImageLoader.DecodeToBitmap(
                    img,
                    new PsxImageLoader.PsxImageLayout(extractionParameter.Width, extractionParameter.Height),
                    new PsxImageLoader.PsxImageFormat(extractionParameter.PixelMode),
                    extractionParameter.PsxClut,
                    i
                );

                _extractedImages.Add($"{extractionParameter.Offset}-{i}", bitmap);
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
