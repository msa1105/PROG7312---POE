using SmartX.Api.Models;
using Xunit;

namespace SmartX.Api.Tests;

public class PowerMetricTests
{
    [Fact]
    public void AdditionOperator_AggregatesWatts()
    {
        var meter1 = new PowerMetric(100.5);
        var meter2 = new PowerMetric(50.25);
        
        var result = meter1 + meter2;
        
        Assert.Equal(150.75, result.Watts);
    }

    [Fact]
    public void SubtractionOperator_CalculatesAbsoluteDifference()
    {
        var meter1 = new PowerMetric(100.0);
        var meter2 = new PowerMetric(25.0);
        
        var result1 = meter1 - meter2;
        var result2 = meter2 - meter1;
        
        Assert.Equal(75.0, result1.Watts);
        Assert.Equal(75.0, result2.Watts);
    }

    [Fact]
    public void ComparisonOperators_WorkCorrectly()
    {
        var meterLarge = new PowerMetric(100.0);
        var meterSmall = new PowerMetric(50.0);
        var meterEqual = new PowerMetric(100.0);
        
        Assert.True(meterLarge > meterSmall);
        Assert.True(meterSmall < meterLarge);
        Assert.True(meterLarge >= meterEqual);
        Assert.True(meterLarge <= meterEqual);
        Assert.False(meterSmall > meterLarge);
    }

    [Fact]
    public void EqualityOperators_WorkCorrectly()
    {
        var meter1 = new PowerMetric(100.0);
        var meter2 = new PowerMetric(100.0);
        var meter3 = new PowerMetric(100.1);
        
        Assert.True(meter1.Equals(meter2));
        Assert.False(meter1.Equals(meter3));
    }
}
