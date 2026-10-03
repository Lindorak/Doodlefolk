using System.Drawing;
using Xunit;

namespace Doodlefolk.Tests;

public class CardArtCropTests
{
    [Fact]
    public void DefaultPreservesExistingFullSizeCrop() =>
        Assert.Equal(new Rectangle(355, 0, 1209, 1080), CardArtCrop.ForThumbnail(1920, 1080));

    [Fact]
    public void DreamSubjectKeepsSleeperAndBubbleInFrame()
    {
        var crop = CardArtCrop.ForThumbnail(1920, 1080, 1535f / 1920);
        Assert.Equal(new Rectangle(711, 0, 1209, 1080), crop);
        // Bounds of the sleeping figure and dream bubble in the checked-in Sweet Dreams art.
        Assert.True(crop.Contains(new Rectangle(1140, 635, 748, 286)));
    }

    [Theory]
    [InlineData(-1f, 0)]
    [InlineData(0f, 0)]
    [InlineData(1f, 711)]
    [InlineData(2f, 711)]
    public void FocusClampsAtImageEdges(float focus, int left) =>
        Assert.Equal(left, CardArtCrop.ForThumbnail(1920, 1080, focus).X);

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidFocusFallsBackToDefault(float focus) =>
        Assert.Equal(CardArtCrop.ForThumbnail(1920, 1080), CardArtCrop.ForThumbnail(1920, 1080, focus));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 200)]
    [InlineData(206, 184)]
    [InlineData(1920, 1080)]
    public void CropIsNonemptyAndInsideImage(int width, int height)
    {
        var crop = CardArtCrop.ForThumbnail(width, height);
        Assert.True(crop.Width > 0 && crop.Height > 0);
        Assert.True(new Rectangle(0, 0, width, height).Contains(crop));
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    public void InvalidDimensionsAreRejected(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CardArtCrop.ForThumbnail(width, height));
}
