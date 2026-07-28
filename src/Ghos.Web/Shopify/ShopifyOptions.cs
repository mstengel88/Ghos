namespace Ghos.Web.Shopify;

public sealed class ShopifyOptions
{
    public const string SectionName = "Shopify";

    public string StoreDomain { get; set; } = "darfaz-2e.myshopify.com";

    public string ApiVersion { get; set; } = "2026-07";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? DraftOrderClientId { get; set; }

    public string? DraftOrderClientSecret { get; set; }

    public string StorefrontUrl { get; set; } = "https://greenhillssupply.com";

    public bool HasEnvironmentCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public bool HasDraftOrderEnvironmentCredentials =>
        !string.IsNullOrWhiteSpace(DraftOrderClientId) &&
        !string.IsNullOrWhiteSpace(DraftOrderClientSecret);
}
