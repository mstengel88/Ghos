using System.Text.RegularExpressions;

namespace Ghos.Web.SmartSearch;

public sealed record SmartSearchCorrection(
    string OriginalQuery,
    string CorrectedQuery,
    IReadOnlyList<string> Replacements);

public static partial class SmartSearchTypoCorrector
{
    private static readonly HashSet<string> ProtectedCustomerWords =
    [
        "available", "buy", "find", "help", "looking", "material",
        "need", "needs", "order", "please", "product", "products",
        "show", "some", "something"
    ];

    public static SmartSearchCorrection? Suggest(
        string? query,
        IEnumerable<string?> vocabularyPhrases)
    {
        var original = query?.Trim() ?? "";
        var normalized = SmartSearchSynonymLibrary.Normalize(original);
        if (normalized.Length < 4)
        {
            return null;
        }

        var vocabulary = vocabularyPhrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .SelectMany(phrase => WordRegex().Matches(
                SmartSearchSynonymLibrary.Normalize(phrase))
                .Select(match => match.Value))
            .Where(word => word.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (vocabulary.Count == 0)
        {
            return null;
        }

        var replacements = new List<string>();
        var corrected = WordRegex().Replace(
            normalized,
            match =>
            {
                var word = match.Value;
                if (word.Length < 4 ||
                    vocabulary.Contains(word) ||
                    ProtectedCustomerWords.Contains(word) ||
                    !word.All(char.IsLetter))
                {
                    return word;
                }

                var maximumDistance = word.Length >= 7 ? 2 : 1;
                var candidates = vocabulary
                    .Where(candidate =>
                        candidate.All(char.IsLetter) &&
                        candidate[0] == word[0] &&
                        Math.Abs(candidate.Length - word.Length) <=
                            maximumDistance)
                    .Select(candidate => new
                    {
                        Word = candidate,
                        Distance = DamerauLevenshteinDistance(
                            word,
                            candidate)
                    })
                    .Where(candidate =>
                        candidate.Distance <= maximumDistance)
                    .OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Word)
                    .ToList();
                if (candidates.Count == 0)
                {
                    return word;
                }

                var bestDistance = candidates[0].Distance;
                var best = candidates
                    .Where(candidate =>
                        candidate.Distance == bestDistance)
                    .Select(candidate => candidate.Word)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
                if (best.Count != 1)
                {
                    return word;
                }

                replacements.Add($"{word} → {best[0]}");
                return best[0];
            });
        return replacements.Count == 0 ||
            string.Equals(
                corrected,
                normalized,
                StringComparison.OrdinalIgnoreCase)
            ? null
            : new SmartSearchCorrection(
                original,
                corrected,
                replacements);
    }

    internal static int DamerauLevenshteinDistance(
        string source,
        string target)
    {
        var distances =
            new int[source.Length + 1, target.Length + 1];
        for (var sourceIndex = 0;
            sourceIndex <= source.Length;
            sourceIndex++)
        {
            distances[sourceIndex, 0] = sourceIndex;
        }

        for (var targetIndex = 0;
            targetIndex <= target.Length;
            targetIndex++)
        {
            distances[0, targetIndex] = targetIndex;
        }

        for (var sourceIndex = 1;
            sourceIndex <= source.Length;
            sourceIndex++)
        {
            for (var targetIndex = 1;
                targetIndex <= target.Length;
                targetIndex++)
            {
                var substitutionCost =
                    source[sourceIndex - 1] ==
                    target[targetIndex - 1]
                        ? 0
                        : 1;
                distances[sourceIndex, targetIndex] = Math.Min(
                    Math.Min(
                        distances[sourceIndex - 1, targetIndex] + 1,
                        distances[sourceIndex, targetIndex - 1] + 1),
                    distances[sourceIndex - 1, targetIndex - 1] +
                        substitutionCost);
                if (sourceIndex > 1 &&
                    targetIndex > 1 &&
                    source[sourceIndex - 1] ==
                        target[targetIndex - 2] &&
                    source[sourceIndex - 2] ==
                        target[targetIndex - 1])
                {
                    distances[sourceIndex, targetIndex] = Math.Min(
                        distances[sourceIndex, targetIndex],
                        distances[
                            sourceIndex - 2,
                            targetIndex - 2] + 1);
                }
            }
        }

        return distances[source.Length, target.Length];
    }

    [GeneratedRegex(@"[\p{L}\p{N}#/]+")]
    private static partial Regex WordRegex();
}
