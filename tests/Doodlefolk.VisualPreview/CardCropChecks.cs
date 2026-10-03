using System.Drawing;
using Doodlefolk;

static class CardCropChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Card crop: " + name);
            checks++;
        }

        var before = CardArtCrop.ForThumbnail(1920, 1080);
        Check(before == new Rectangle(355, 0, 1209, 1080), "default crop unchanged");
        var after = CardArtCrop.ForThumbnail(1920, 1080, 1535f / 1920);
        var subject = new Rectangle(1140, 635, 748, 286);
        Check(!before.Contains(subject) && after.Contains(subject), "Sweet Dreams subject fits");
        Check(after == new Rectangle(711, 0, 1209, 1080), "right-edge crop");

        foreach (int width in new[] { 1, 100, 206, 1920 })
        foreach (int height in new[] { 1, 184, 1080, 2000 })
        foreach (float focus in new[] { -1f, 0f, 0.5f, 0.8f, 1f, 2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var crop = CardArtCrop.ForThumbnail(width, height, focus);
            Check(crop.Width > 0 && crop.Height > 0 && new Rectangle(0, 0, width, height).Contains(crop), "valid source bounds");
            if (!float.IsFinite(focus))
                Check(crop == CardArtCrop.ForThumbnail(width, height), "invalid focus fallback");
        }
        foreach (var (width, height) in new[] { (0, 1080), (1920, 0), (-1, 1080), (1920, -1) })
        {
            bool rejected = false;
            try { CardArtCrop.ForThumbnail(width, height); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "invalid dimensions rejected");
        }

        Console.WriteLine($"PASS card crop: {checks} checks; before={before}; after={after}");
    }
}
