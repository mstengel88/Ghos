using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class BackupStatusRecord
{
    [Key]
    [MaxLength(40)]
    public string Source { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(24)]
    public string State { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Operation { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? Host { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastSuccessfulBackupAtUtc { get; set; }

    public DateTime? LastFailureAtUtc { get; set; }
}
