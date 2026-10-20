using System.Globalization;

namespace Sepiabox.Worker.Places;

/// <summary>
/// Refuses a download that cannot be the whole of cities500 (S-059, D-067): fewer cities than a floor, or many
/// fewer than the table holds now. GeoNames' files cannot be pinned, so this is what catches a truncated or
/// wrong file before it replaces the cities.
/// </summary>
/// <param name="MinCities">cities500 has about 200,000 cities; far fewer means a broken file.</param>
/// <param name="MaxDrop">The largest drop from the previous load accepted, as a fraction.</param>
public sealed record GeoNamesLoadGuard(int MinCities, double MaxDrop)
{
    /// <summary>The guard of <c>geonames update</c>: at least 150,000 cities, and at most 10% fewer than before.</summary>
    public static GeoNamesLoadGuard Default { get; } = new(150_000, 0.10);

    /// <summary>Throws when <paramref name="cities"/> must not replace the <paramref name="previous"/> ones.</summary>
    public void Check(int cities, int previous)
    {
        if (cities < MinCities)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The download has {cities} cities, fewer than the {MinCities} expected at least; the {previous} cities loaded are kept."));
        }

        if (previous > 0 && cities < previous * (1 - MaxDrop))
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The download has {cities} cities, more than {MaxDrop:P0} fewer than the {previous} loaded now; they are kept."));
        }
    }
}
