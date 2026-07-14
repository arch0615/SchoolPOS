using FluentAssertions;
using SchoolPOS.Domain.Common;

namespace SchoolPOS.Domain.Tests;

public class CommissionCalculatorTests
{
    [Theory]
    [InlineData(100, 0.05, 5.00)]
    [InlineData(250, 0.05, 12.50)]
    [InlineData(100, 0.00, 0.00)]     // comisión puede ser 0 (FR-COM-6)
    [InlineData(33.33, 0.05, 1.67)]   // redondeo comercial a 2 decimales
    public void Compute_returns_expected_commission(decimal amount, decimal rate, decimal expected)
    {
        CommissionCalculator.Compute(amount, rate).Should().Be(expected);
    }

    [Fact]
    public void Rate_out_of_range_throws()
    {
        var act = () => CommissionCalculator.Compute(100m, 1.5m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
