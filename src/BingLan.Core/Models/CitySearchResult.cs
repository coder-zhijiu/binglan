using System.Globalization;

namespace BingLan.Core.Models;

public sealed record CitySearchResult(
    long Id,
    string Name,
    string Admin1,
    string Country,
    double Latitude,
    double Longitude)
{
    public string DisplayName => string.Join(
        " · ",
        new[] { Name, Admin1, Country }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    public string CoordinateText => string.Create(
        CultureInfo.InvariantCulture,
        $"{Latitude:0.####}, {Longitude:0.####}");
}

public sealed record CitySearchOutcome(
    bool Succeeded,
    IReadOnlyList<CitySearchResult> Results,
    string Message);
