using Ghos.Web.DumpSite;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class DumpSiteMaintenanceCommandTests
{
    [Fact]
    public void ValidateDestinationAcceptsProtectedTemporaryPath()
    {
        var result = DumpSiteMaintenanceCommand.ValidateDestination(
            "/tmp/ghos-maintenance/dumpsite-secret");

        Assert.Equal(
            "/tmp/ghos-maintenance/dumpsite-secret",
            result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/tmp/dumpsite-secret")]
    [InlineData("/tmp/ghos-maintenance")]
    [InlineData("/tmp/ghos-maintenance/../dumpsite-secret")]
    [InlineData("/etc/ghos-backup/backup.env")]
    public void ValidateDestinationRejectsUnsafePath(string path)
    {
        Assert.Throws<InvalidOperationException>(() =>
            DumpSiteMaintenanceCommand.ValidateDestination(path));
    }
}
