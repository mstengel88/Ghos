namespace Ghos.Web.WinterWatch;

public sealed class WinterWatchAdminOptions
{
    public const string SectionName = "WinterWatchAdmin";

    public string FunctionUrl { get; set; } = string.Empty;

    public string IntegrationSecret { get; set; } = string.Empty;

    public string InviteRedirectUrl { get; set; } =
        "https://winterwatch-pro.info/auth/callback";
}
