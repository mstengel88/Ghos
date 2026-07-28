namespace Ghos.Web.ProjectTools;

public sealed class DispatchQuoteDataOptions
{
    public const string SectionName = "DispatchQuoteData";

    public string SupabaseUrl { get; set; } = string.Empty;

    public string ServiceRoleKey { get; set; } = string.Empty;

    public int RefreshMinutes { get; set; } = 30;
}
