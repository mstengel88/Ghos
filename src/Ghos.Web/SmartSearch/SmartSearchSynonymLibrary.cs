using System.Text.RegularExpressions;

namespace Ghos.Web.SmartSearch;

internal sealed record SmartSearchConcept(
    string Category,
    string Name,
    IReadOnlyList<string> Terms);

public sealed record SmartSearchQueryPlan(
    string OriginalQuery,
    string NormalizedQuery,
    IReadOnlySet<string> DirectTerms,
    IReadOnlySet<string> ExpandedTerms,
    IReadOnlyList<string> Intents);

public static partial class SmartSearchSynonymLibrary
{
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "and", "for", "i", "in", "me", "my", "of", "on",
        "or", "the", "to", "want", "with"
    ];

    private static readonly SmartSearchConcept[] Concepts =
    [
        new("Use", "Driveways", ["driveway", "gravel drive", "parking pad", "vehicle access", "private road", "drive base", "drive topping"]),
        new("Use", "Patios", ["patio", "terrace", "courtyard", "outdoor seating", "entertaining area", "outdoor room", "backyard living"]),
        new("Use", "Drainage", ["drainage", "french drain", "water management", "runoff", "pipe bedding", "drain field", "backfill"]),
        new("Use", "Walkways", ["walkway", "path", "garden path", "footpath", "sidewalk", "walking trail", "stepping path"]),
        new("Use", "Retaining walls", ["retaining wall", "landscape wall", "garden wall", "seat wall", "wall block", "grade change", "slope support"]),
        new("Use", "Foundations", ["base", "foundation", "subbase", "compaction", "structural fill", "road base", "paver base"]),
        new("Use", "Gardens", ["garden", "garden bed", "raised bed", "planting bed", "vegetable garden", "flower bed", "landscape bed"]),
        new("Use", "Erosion control", ["erosion", "slope stabilization", "washout", "shoreline", "ditch lining", "soil retention", "runoff control"]),
        new("Use", "Play areas", ["playground", "play area", "swing set", "playset", "kids area", "play surface", "recreation area"]),
        new("Use", "Fire features", ["fire pit", "firepit", "fire ring", "outdoor fireplace", "burn area", "fire feature", "campfire area"]),
        new("Project", "Pavers", ["paver", "pavers", "brick patio", "hardscape", "segmental paving", "paving stone", "patio block"]),
        new("Project", "Concrete", ["concrete", "concrete prep", "slab base", "flatwork", "cement work", "footing", "concrete bedding"]),
        new("Project", "Landscaping", ["landscaping", "landscape project", "yard project", "property improvement", "outdoor project", "grounds", "yard renovation"]),
        new("Project", "Winter maintenance", ["winter", "snow", "ice", "deicing", "de-icing", "slip prevention", "winter maintenance"]),
        new("Material", "Crushed stone", ["stone", "crushed stone", "crushed rock", "limestone", "aggregate", "road stone", "base stone", "crusher stone"]),
        new("Material", "Decorative stone", ["decorative stone", "landscape rock", "ornamental stone", "garden stone", "accent rock", "decorative rock", "river rock"]),
        new("Material", "Mulch", ["mulch", "wood chips", "bark", "shredded bark", "landscape mulch", "ground cover", "wood mulch"]),
        new("Material", "Soil", ["soil", "dirt", "topsoil", "garden soil", "planting mix", "earth", "screened soil"]),
        new("Material", "Sand", ["sand", "masonry sand", "bedding sand", "play sand", "fill sand", "washed sand", "joint sand"]),
        new("Material", "Salt", ["salt", "rock salt", "road salt", "deicer", "ice melt", "deicing salt", "winter salt"]),
        new("Color", "Gray", ["gray", "grey", "charcoal", "slate", "silver", "granite color", "neutral gray"]),
        new("Color", "Brown", ["brown", "tan", "buff", "earth tone", "beige", "sand color", "natural brown"]),
        new("Color", "Red", ["red", "brick red", "rust", "burgundy", "terracotta", "cranberry", "warm red"]),
        new("Color", "Black", ["black", "dark", "ebony", "midnight", "jet black", "deep charcoal", "blackened"]),
        new("Color", "White", ["white", "bright", "ivory", "cream", "light stone", "pale", "off white"]),
        new("Size", "Small", ["small", "fine", "pea size", "pea gravel", "3/8", "quarter inch", "screenings"]),
        new("Size", "Medium", ["medium", "1/2", "3/4", "one inch", "number 1 stone", "#1 stone", "mid size"]),
        new("Size", "Large", ["large", "oversized", "2 inch", "3 inch", "cobble", "boulder", "outcropping"]),
        new("Fulfillment", "Bulk", ["bulk", "loose", "by the yard", "by the ton", "truckload", "loader bucket", "bulk delivery"]),
        new("Fulfillment", "Bagged", ["bagged", "bag", "sack", "pallet of bags", "retail bag", "small quantity", "take home"]),
        new("Fulfillment", "Pickup", ["pickup", "pick up", "will call", "yard pickup", "self haul", "collect", "load my truck"]),
        new("Fulfillment", "Delivery", ["delivery", "deliver", "drop off", "truck delivery", "jobsite delivery", "home delivery", "bring it"]),
    ];

    public static int SynonymMappingCount =>
        Concepts.Sum(concept =>
            concept.Terms.Count * (concept.Terms.Count - 1));

    public static SmartSearchQueryPlan Plan(string? query)
    {
        var normalized = Normalize(query);
        var direct = new HashSet<string>(
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(term => !StopWords.Contains(term)),
            StringComparer.OrdinalIgnoreCase);
        if (normalized.Length > 0)
        {
            direct.Add(normalized);
        }

        var expanded = new HashSet<string>(
            direct,
            StringComparer.OrdinalIgnoreCase);
        var intents = new List<string>();
        foreach (var concept in Concepts.Where(concept =>
            concept.Terms.Any(term =>
                ContainsTerm(normalized, Normalize(term)))))
        {
            foreach (var term in concept.Terms)
            {
                expanded.Add(Normalize(term));
            }

            intents.Add($"{concept.Category}: {concept.Name}");
        }

        return new SmartSearchQueryPlan(
            query?.Trim() ?? "",
            normalized,
            direct,
            expanded,
            intents.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static string Normalize(string? value) =>
        WhitespaceRegex().Replace(
            NonSearchCharacterRegex().Replace(
                (value ?? "").ToLowerInvariant(),
                " "),
            " ").Trim();

    private static bool ContainsTerm(string query, string term) =>
        query.Equals(term, StringComparison.OrdinalIgnoreCase) ||
        query.Contains($" {term} ", StringComparison.OrdinalIgnoreCase) ||
        query.StartsWith($"{term} ", StringComparison.OrdinalIgnoreCase) ||
        query.EndsWith($" {term}", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"[^a-z0-9#&/.\-]+")]
    private static partial Regex NonSearchCharacterRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
