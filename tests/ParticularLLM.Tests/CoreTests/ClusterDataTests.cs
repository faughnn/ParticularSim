using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

public class ClusterDataTests
{
    [Fact]
    public void ClusterData_HoldsPixels()
    {
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(1, 0, Materials.Stone));
        Assert.Equal(2, cluster.PixelCount);
    }

    [Fact]
    public void ClusterData_TracksPosition()
    {
        var cluster = new ClusterData(1);
        cluster.X = 100f;
        cluster.Y = 50f;
        cluster.Rotation = 0.5f;

        Assert.Equal(100f, cluster.X);
        Assert.Equal(50f, cluster.Y);
        Assert.Equal(0.5f, cluster.Rotation);
    }

    [Fact]
    public void ClusterData_TracksVelocity()
    {
        var cluster = new ClusterData(1);
        cluster.VelocityX = 10f;
        cluster.VelocityY = -5f;

        Assert.Equal(10f, cluster.VelocityX);
        Assert.Equal(-5f, cluster.VelocityY);
    }

    [Fact]
    public void ClusterData_PixelsAreReadable()
    {
        var cluster = new ClusterData(42);
        cluster.AddPixel(new ClusterPixel(3, 4, Materials.Sand));

        Assert.Equal(42, cluster.Id);
        Assert.Equal(1, cluster.Pixels.Count);
        Assert.Equal(3, cluster.Pixels[0].localX);
        Assert.Equal(4, cluster.Pixels[0].localY);
        Assert.Equal(Materials.Sand, cluster.Pixels[0].materialId);
    }
}
