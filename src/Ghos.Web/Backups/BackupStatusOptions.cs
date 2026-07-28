namespace Ghos.Web.Backups;

public sealed class BackupStatusOptions
{
    public const string SectionName = "BackupStatus";

    public string IntegrationSecret { get; set; } = string.Empty;
}
