using System.Globalization;
using Sepiabox.Web.Localization;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class UiTextTests
{
    [Fact]
    public void Has_brazilian_portuguese_culture_data_when_globalization_is_not_invariant()
    {
        // The web turns InvariantGlobalization off and needs ICU (D-058): without it this throws or gives "July".
        Assert.Equal("julho", CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.MonthNames[6]);
    }

    [Theory]
    [InlineData(Cultures.English, "July 2019")]
    [InlineData(Cultures.BrazilianPortuguese, "julho de 2019")]
    public void Writes_a_month_heading_in_the_culture_when_the_month_is_july_2019(string culture, string expected)
    {
        using var scope = Cultures.Use(culture);

        Assert.Equal(expected, Cultures.Text().MonthAndYear(new DateOnly(2019, 7, 1)));
    }

    [Theory]
    [InlineData(Cultures.English, "July 14, 2019, 10:32")]
    [InlineData(Cultures.BrazilianPortuguese, "14 de julho de 2019, 10:32")]
    public void Writes_a_date_and_time_in_the_culture(string culture, string expected)
    {
        using var scope = Cultures.Use(culture);

        Assert.Equal(expected, Cultures.Text().DateAndTime(new DateTime(2019, 7, 14, 10, 32, 5)));
    }

    [Theory]
    [InlineData(Cultures.English, "March 2024", "December 3, 2024, 09:05")]
    [InlineData(Cultures.BrazilianPortuguese, "março de 2024", "3 de dezembro de 2024, 09:05")]
    public void Writes_other_months_and_single_digit_days_in_the_culture(string culture, string month, string dateAndTime)
    {
        using var scope = Cultures.Use(culture);
        var text = Cultures.Text();

        Assert.Equal(month, text.MonthAndYear(new DateOnly(2024, 3, 1)));
        Assert.Equal(dateAndTime, text.DateAndTime(new DateTime(2024, 12, 3, 9, 5, 0)));
    }

    [Theory]
    [InlineData(Cultures.English, "No date", "Photo", "Video")]
    [InlineData(Cultures.BrazilianPortuguese, "Sem data", "Foto", "Vídeo")]
    public void Names_undated_assets_and_kinds_in_the_culture(string culture, string noDate, string photo, string video)
    {
        using var scope = Cultures.Use(culture);
        var text = Cultures.Text();

        Assert.Equal(noDate, text.DateOrNoDate(null));
        Assert.Equal(photo, text.KindLabel(AssetKinds.Photo));
        Assert.Equal(video, text.KindLabel(AssetKinds.Video));
    }
}
