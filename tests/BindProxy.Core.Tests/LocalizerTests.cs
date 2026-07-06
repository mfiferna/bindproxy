using System.Globalization;
using BindProxy.Core.Localization;
using Xunit;

namespace BindProxy.Core.Tests;

public class LocalizerTests
{
    [Fact]
    public void Normalizes_czech_to_czech()
    {
        var culture = Localizer.NormalizeCulture(CultureInfo.GetCultureInfo("cs-CZ"));
        Assert.Equal("cs-CZ", culture.Name);
    }

    [Fact]
    public void Normalizes_other_languages_to_english()
    {
        var culture = Localizer.NormalizeCulture(CultureInfo.GetCultureInfo("de-DE"));
        Assert.Equal("en-US", culture.Name);
    }

    [Fact]
    public void Can_switch_to_english_and_back()
    {
        var original = Localizer.CurrentCulture;
        try
        {
            Localizer.SetCulture(CultureInfo.GetCultureInfo("en-US"));
            Assert.Equal("Refresh", Localizer.Get(TextKey.Refresh));
        }
        finally
        {
            Localizer.SetCulture(original);
        }
    }

    [Theory]
    [InlineData("cs", "cs-CZ")]
    [InlineData("en", "en-US")]
    [InlineData("cs-CZ", "cs-CZ")]
    [InlineData("en-US", "en-US")]
    public void Parses_supported_language_codes(string code, string expectedCulture)
    {
        var original = Localizer.CurrentCulture;
        try
        {
            Assert.True(Localizer.TrySetCulture(code));
            Assert.Equal(expectedCulture, Localizer.CurrentCulture.Name);
        }
        finally
        {
            Localizer.SetCulture(original);
        }
    }
}
