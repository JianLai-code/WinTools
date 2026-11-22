using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

public class UuidGeneratorTests
{
    [Fact]
    public void Generate_Default_ReturnsRequestedCountOfValidGuids()
    {
        var values = UuidGenerator.Generate(10);
        Assert.Equal(10, values.Length);
        Assert.Equal(10, values.Distinct().Count());
        Assert.All(values, v => Assert.True(Guid.TryParse(v, out _)));
        Assert.All(values, v => Assert.Equal(36, v.Length));
    }

    [Theory]
    [InlineData(UuidFormat.Dashed, 36)]
    [InlineData(UuidFormat.NoHyphens, 32)]
    [InlineData(UuidFormat.Braces, 38)]
    public void Format_ShapesAreCorrect(UuidFormat format, int length)
    {
        var guid = Guid.Parse("12345678-1234-5678-9abc-def012345678");
        var s = UuidGenerator.Format(guid, format, false);
        Assert.Equal(length, s.Length);
        Assert.Equal(format == UuidFormat.Braces ? '{' : '1', s[0]);
    }

    [Fact]
    public void Format_Uppercase_ChangesOnlyCase()
    {
        var guid = Guid.Parse("abcdefab-1234-5678-9abc-def012345678");
        var lower = UuidGenerator.Format(guid, UuidFormat.Dashed, false);
        var upper = UuidGenerator.Format(guid, UuidFormat.Dashed, true);
        Assert.Equal(lower.ToUpperInvariant(), upper);
        Assert.True(Guid.TryParse(upper, out _));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9999, UuidGenerator.MaxCount)]
    public void ClampCount_StaysInRange(int input, int expected) =>
        Assert.Equal(expected, UuidGenerator.ClampCount(input));
}
