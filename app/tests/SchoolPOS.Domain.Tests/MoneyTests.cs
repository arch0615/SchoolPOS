using FluentAssertions;
using SchoolPOS.Domain.Common;

namespace SchoolPOS.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Add_and_subtract_same_currency()
    {
        var a = new Money(100m, "MXN");
        var b = new Money(30m, "MXN");
        (a + b).Amount.Should().Be(130m);
        (a - b).Amount.Should().Be(70m);
    }

    [Fact]
    public void Round_uses_commercial_rounding_to_two_decimals()
    {
        new Money(5.005m, "MXN").Round().Amount.Should().Be(5.01m);
        new Money(2.994m, "MXN").Round().Amount.Should().Be(2.99m);
    }

    [Fact]
    public void Operating_across_currencies_throws()
    {
        var mxn = new Money(10m, "MXN");
        var usd = new Money(10m, "USD");
        var act = () => { _ = mxn + usd; };
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Commission_of_five_percent_on_100_is_5()
    {
        var topUp = new Money(100m, "MXN");
        (topUp * 0.05m).Round().Amount.Should().Be(5m);
    }
}
