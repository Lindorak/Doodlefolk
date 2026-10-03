using System.Drawing;

namespace Doodlefolk;

/// <summary>Source rectangle for a 206×184 Steam card, with an optional horizontal subject focus.</summary>
static class CardArtCrop
{
    // centerX is relative to the captured image, not the desktop. The default preserves existing cards.
    public static Rectangle ForThumbnail(int width, int height, float centerX = 0.5f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(centerX)) centerX = 0.5f;

        float scale = Math.Max(206f / width, 184f / height);
        int cropWidth = Math.Clamp((int)(206 / scale), 1, width);
        int cropHeight = Math.Clamp((int)(184 / scale), 1, height);
        int left = (int)Math.Clamp(width * centerX - cropWidth / 2f, 0, width - cropWidth);
        int top = height - cropHeight - (height - cropHeight) / 4;
        return new Rectangle(left, top, cropWidth, cropHeight);
    }
}
