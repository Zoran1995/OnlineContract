using Xunit;

namespace OnlineContract.Tests;

public class VariantTotalCalcTests
{
    private static decimal ComputeTotal(decimal amount, int qty)
    {
        var net = amount * qty;
        return Math.Round(net, 2, MidpointRounding.AwayFromZero);
    }

    [Fact]
    public void Computes_Total_Net_Without_Tax()
    {
        Assert.Equal(100.00m, ComputeTotal(100m, 1));
        Assert.Equal(299.98m, ComputeTotal(149.99m, 2));
        Assert.Equal(0.00m, ComputeTotal(0m, 5));
        Assert.Equal(599.85m, ComputeTotal(199.95m, 3));
    }
}
