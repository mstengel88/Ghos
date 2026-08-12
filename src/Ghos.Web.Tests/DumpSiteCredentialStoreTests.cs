using Ghos.Web.DumpSite;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class DumpSiteCredentialStoreTests
{
    [Theory]
    [InlineData(
        "https://example.supabase.co/functions/v1/dump-site-bridge/",
        "https://example.supabase.co/functions/v1/dump-site-bridge")]
    [InlineData(
        DumpSiteCredentialStore.LocalOperationsBridgeUrl,
        DumpSiteCredentialStore.LocalOperationsBridgeUrl)]
    public void NormalizeBaseUrl_AllowsSupportedBridgeEndpoints(
        string input,
        string expected)
    {
        Assert.Equal(
            expected,
            DumpSiteCredentialStore.NormalizeBaseUrl(input));
    }

    [Theory]
    [InlineData("http://example.com/functions/v1/dump-site-bridge")]
    [InlineData("http://ghos-operations-api:8001/functions/v1/dump-site-bridge")]
    [InlineData("https://user:password@example.com/dump-site-bridge")]
    [InlineData("not-a-url")]
    public void NormalizeBaseUrl_RejectsUntrustedOrInvalidEndpoints(
        string input)
    {
        Assert.Throws<DumpSiteConnectionException>(
            () => DumpSiteCredentialStore.NormalizeBaseUrl(input));
    }
}
