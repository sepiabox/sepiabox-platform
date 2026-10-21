using System.Linq.Expressions;
using NetTopologySuite.Geometries;

namespace Sepiabox.Catalog.Search;

/// <summary>
/// The family's places in a search (S-014, D-083), the PostGIS way (D-051): NetTopologySuite's
/// <c>IsWithinDistance</c>, which Npgsql turns into <c>ST_DWithin</c> on the geography columns, so distances are metres
/// on the spheroid. The places are read first and each one's centre and radius sent as values, so every
/// <c>ST_DWithin(location, @centre, @metres)</c> can use the GiST index on <c>assets.location</c>; assets without a
/// location never match.
/// </summary>
public static class PostgisDistance
{
    /// <summary>Assets taken inside any of the places: within each one's own radius of its centre.</summary>
    public static IQueryable<Asset> InAny(IQueryable<Asset> assets, IEnumerable<SavedPlace> places)
    {
        ArgumentNullException.ThrowIfNull(places);
        var parameter = Expression.Parameter(typeof(Asset), "a");
        Expression? any = null;
        foreach (var place in places)
        {
            var within = Within(place.Location, place.RadiusM);
            var body = new Replace(within.Parameters[0], parameter).Visit(within.Body);
            any = any is null ? body : Expression.OrElse(any, body);
        }

        return any is null ? assets.Where(_ => false) : assets.Where(Expression.Lambda<Func<Asset, bool>>(any, parameter));
    }

    /// <summary>
    /// Assets taken more than <paramref name="minMeters"/> and at most <paramref name="maxMeters"/> from
    /// <paramref name="centre"/> (each bound when given). Only the upper bound can use the index: "farther than" reads
    /// every asset with a location.
    /// </summary>
    public static IQueryable<Asset> Between(IQueryable<Asset> assets, Point centre, double? minMeters, double? maxMeters)
    {
        assets = assets.Where(a => a.Location != null);
        if (maxMeters is { } max)
        {
            assets = assets.Where(Within(centre, max));
        }

        if (minMeters is { } min)
        {
            assets = assets.Where(a => !a.Location!.IsWithinDistance(centre, min));
        }

        return assets;
    }

    private static Expression<Func<Asset, bool>> Within(Point centre, double metres) =>
        a => a.Location != null && a.Location.IsWithinDistance(centre, metres);

    private sealed class Replace(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
