using System.Globalization;
using System.Text.Json;

namespace Ghos.Web.ProjectTools;

public sealed record QuoteTaxAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);

public sealed record QuoteTaxRateMatch(
    decimal Rate,
    string Label,
    bool MatchedRule);

public sealed class QuoteTaxCalculator
{
    private const decimal DefaultRate = .055m;
    private readonly decimal _fallbackRate;
    private readonly IReadOnlyList<QuoteTaxRule> _rules;

    public QuoteTaxCalculator(IConfiguration configuration)
    {
        _fallbackRate =
            ParseRate(configuration["QUOTE_TAX_RATE"]) ?? DefaultRate;
        _rules = BuildRules(configuration);
    }

    public QuoteTaxRateMatch Resolve(QuoteTaxAddress address)
    {
        foreach (var rule in _rules)
        {
            if (RuleMatches(rule, address))
            {
                return new QuoteTaxRateMatch(
                    rule.Rate,
                    string.IsNullOrWhiteSpace(rule.Label)
                        ? "Local tax"
                        : rule.Label.Trim(),
                    true);
            }
        }

        return new QuoteTaxRateMatch(
            _fallbackRate,
            "Default tax",
            false);
    }

    private static IReadOnlyList<QuoteTaxRule> BuildRules(
        IConfiguration configuration)
    {
        var rules = new List<QuoteTaxRule>();
        var cityRate = ParseRate(
            configuration["QUOTE_MILWAUKEE_CITY_TAX_RATE"]);
        var countyRate = ParseRate(
            configuration["QUOTE_MILWAUKEE_COUNTY_TAX_RATE"]);

        if (cityRate is not null)
        {
            rules.Add(new QuoteTaxRule
            {
                Label = "Milwaukee city",
                Rate = cityRate.Value,
                Cities = ["Milwaukee"],
                State = "WI",
                Country = "US"
            });
        }

        if (countyRate is not null)
        {
            var cities = SplitCsv(
                configuration["QUOTE_MILWAUKEE_COUNTY_CITIES"]);
            var postalCodes = SplitCsv(
                configuration["QUOTE_MILWAUKEE_COUNTY_POSTAL_CODES"]);
            var postalCodePrefixes = SplitCsv(
                configuration["QUOTE_MILWAUKEE_COUNTY_POSTAL_PREFIXES"]);

            if (cities.Count > 0 ||
                postalCodes.Count > 0 ||
                postalCodePrefixes.Count > 0)
            {
                rules.Add(new QuoteTaxRule
                {
                    Label = "Milwaukee County",
                    Rate = countyRate.Value,
                    Cities = cities,
                    PostalCodes = postalCodes,
                    PostalCodePrefixes = postalCodePrefixes,
                    State = "WI",
                    Country = "US"
                });
            }
        }

        var rawRules = configuration["QUOTE_TAX_RATE_RULES"]?.Trim();
        if (string.IsNullOrWhiteSpace(rawRules))
        {
            return rules;
        }

        try
        {
            var configuredRules =
                JsonSerializer.Deserialize<List<ConfiguredQuoteTaxRule>>(
                    rawRules,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            if (configuredRules is null)
            {
                return rules;
            }

            foreach (var configuredRule in configuredRules)
            {
                var rate = ParseRate(configuredRule.Rate);
                if (rate is null)
                {
                    continue;
                }

                rules.Add(new QuoteTaxRule
                {
                    Label = configuredRule.Label,
                    Rate = rate.Value,
                    Cities = configuredRule.Cities ?? [],
                    PostalCodes = configuredRule.PostalCodes ?? [],
                    PostalCodePrefixes =
                        configuredRule.PostalCodePrefixes ?? [],
                    State = configuredRule.Province,
                    Country = configuredRule.Country,
                    AddressIncludes = configuredRule.AddressIncludes ?? []
                });
            }
        }
        catch (JsonException)
        {
            // Invalid optional custom rules must not prevent quote creation.
        }

        return rules;
    }

    private static bool RuleMatches(
        QuoteTaxRule rule,
        QuoteTaxAddress address)
    {
        var city = NormalizeText(ParseCity(address));
        var fullCity = NormalizeText(address.City);
        var state = NormalizeText(address.State);
        var country = NormalizeText(address.Country ?? "US");
        var postalCode = ParsePostalCode(address);
        var fullAddress = NormalizeText(string.Join(
            ' ',
            new[]
            {
                address.AddressLine1,
                address.AddressLine2,
                address.City,
                address.State,
                address.PostalCode,
                address.Country
            }.Where(value => !string.IsNullOrWhiteSpace(value))));

        if (!string.IsNullOrWhiteSpace(rule.State) &&
            !string.IsNullOrWhiteSpace(state) &&
            NormalizeText(rule.State) != state)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.Country) &&
            !string.IsNullOrWhiteSpace(country) &&
            NormalizeText(rule.Country) != country)
        {
            return false;
        }

        var cities = rule.Cities
            .Select(NormalizeText)
            .Where(value => value.Length > 0)
            .ToList();
        var postalCodes = rule.PostalCodes
            .Select(NormalizePostalCode)
            .Where(value => value.Length > 0)
            .ToList();
        var postalPrefixes = rule.PostalCodePrefixes
            .Select(NormalizePostalCode)
            .Where(value => value.Length > 0)
            .ToList();
        var addressIncludes = rule.AddressIncludes
            .Select(NormalizeText)
            .Where(value => value.Length > 0)
            .ToList();

        if (cities.Count == 0 &&
            postalCodes.Count == 0 &&
            postalPrefixes.Count == 0 &&
            addressIncludes.Count == 0)
        {
            return true;
        }

        return cities.Any(candidate =>
                   city == candidate || fullCity.Contains(candidate)) ||
               postalCodes.Any(candidate => postalCode == candidate) ||
               postalPrefixes.Any(postalCode.StartsWith) ||
               addressIncludes.Any(fullAddress.Contains);
    }

    private static string ParseCity(QuoteTaxAddress address)
    {
        if (!string.IsNullOrWhiteSpace(address.City))
        {
            return address.City.Split(',')[0].Trim();
        }

        var parts = new[]
            {
                address.AddressLine1,
                address.AddressLine2
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(','))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();
        return parts.Count >= 2 ? parts[^2] : string.Empty;
    }

    private static string ParsePostalCode(QuoteTaxAddress address)
    {
        var direct = NormalizePostalCode(address.PostalCode);
        if (direct.Length > 0)
        {
            return direct;
        }

        var addressText = string.Join(
            ' ',
            new[]
            {
                address.AddressLine1,
                address.AddressLine2,
                address.City
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var match = System.Text.RegularExpressions.Regex.Match(
            addressText,
            @"\b\d{5}(?:-\d{4})?\b");
        return match.Success
            ? NormalizePostalCode(match.Value)
            : string.Empty;
    }

    private static decimal? ParseRate(string? rawRate)
    {
        if (!decimal.TryParse(
                rawRate,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var rate) ||
            rate < 0)
        {
            return null;
        }

        return rate > 1m ? rate / 100m : rate;
    }

    private static decimal? ParseRate(JsonElement rawRate) =>
        rawRate.ValueKind switch
        {
            JsonValueKind.Number when rawRate.TryGetDecimal(out var rate) =>
                rate > 1m ? rate / 100m : rate,
            JsonValueKind.String => ParseRate(rawRate.GetString()),
            _ => null
        };

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        (value ?? string.Empty)
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

    private static string NormalizeText(string? value) =>
        string.Join(
                ' ',
                (value ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant()
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries))
            .Trim();

    private static string NormalizePostalCode(string? value) =>
        string.Concat(
            (value ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Where(character => !char.IsWhiteSpace(character)));

    private sealed class QuoteTaxRule
    {
        public string? Label { get; init; }

        public decimal Rate { get; init; }

        public IReadOnlyList<string> Cities { get; init; } = [];

        public IReadOnlyList<string> PostalCodes { get; init; } = [];

        public IReadOnlyList<string> PostalCodePrefixes { get; init; } = [];

        public string? State { get; init; }

        public string? Country { get; init; }

        public IReadOnlyList<string> AddressIncludes { get; init; } = [];
    }

    private sealed class ConfiguredQuoteTaxRule
    {
        public string? Label { get; init; }

        public JsonElement Rate { get; init; }

        public List<string>? Cities { get; init; }

        public List<string>? PostalCodes { get; init; }

        public List<string>? PostalCodePrefixes { get; init; }

        public string? Province { get; init; }

        public string? Country { get; init; }

        public List<string>? AddressIncludes { get; init; }
    }
}
