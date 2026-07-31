using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Ghos.Web.WinterWatch;

public sealed class WinterWatchAdminResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("organizations")]
    public List<WinterWatchOrganizationSummary> Organizations { get; set; } = [];

    [JsonPropertyName("workspace")]
    public WinterWatchWorkspace? Workspace { get; set; }

    [JsonPropertyName("organization")]
    public WinterWatchOrganizationSummary? Organization { get; set; }

    [JsonPropertyName("user")]
    public WinterWatchWorkspaceUser? User { get; set; }

    [JsonPropertyName("account")]
    public WinterWatchServiceAccount? Account { get; set; }
}

public sealed class WinterWatchOrganizationSummary
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("plan")]
    public string Plan { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("user_count")]
    public int UserCount { get; set; }

    [JsonPropertyName("account_count")]
    public int AccountCount { get; set; }

    [JsonPropertyName("employee_count")]
    public int EmployeeCount { get; set; }
}

public sealed class WinterWatchWorkspace
{
    [JsonPropertyName("organization")]
    public WinterWatchOrganizationSummary Organization { get; set; } = new();

    [JsonPropertyName("users")]
    public List<WinterWatchWorkspaceUser> Users { get; set; } = [];

    [JsonPropertyName("accounts")]
    public List<WinterWatchServiceAccount> Accounts { get; set; } = [];
}

public sealed class WinterWatchWorkspaceUser
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("employee_id")]
    public Guid? EmployeeId { get; set; }

    [JsonPropertyName("employee_active")]
    public bool? EmployeeActive { get; set; }
}

public sealed class WinterWatchServiceAccount
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }

    [JsonPropertyName("contact_name")]
    public string? ContactName { get; set; }

    [JsonPropertyName("contact_phone")]
    public string? ContactPhone { get; set; }

    [JsonPropertyName("contact_email")]
    public string? ContactEmail { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 5;

    [JsonPropertyName("geofence_radius")]
    public int GeofenceRadius { get; set; } = 100;

    [JsonPropertyName("service_type")]
    public string ServiceType { get; set; } = "both";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
}

public sealed class WinterWatchOnboardingInput
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string OrganizationName { get; set; } = string.Empty;

    [StringLength(50)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    public string Plan { get; set; } = "launch";

    [Required]
    public string Status { get; set; } = "active";

    [Required, StringLength(120, MinimumLength = 2)]
    public string ContactName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string ContactEmail { get; set; } = string.Empty;

    [Phone]
    public string ContactPhone { get; set; } = string.Empty;

    [Required]
    public string ContactRole { get; set; } = "admin";

    public bool CreateEmployee { get; set; }

    public string EmployeeCategory { get; set; } = "manager";

    public bool AddFirstAccount { get; set; } = true;

    public WinterWatchAccountInput Account { get; set; } = new();
}

public sealed class WinterWatchAccountInput
{
    public Guid? Id { get; set; }

    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(250, MinimumLength = 3)]
    public string Address { get; set; } = string.Empty;

    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    [StringLength(2, MinimumLength = 2)]
    public string State { get; set; } = "WI";

    [StringLength(10)]
    public string Zip { get; set; } = string.Empty;

    [StringLength(120)]
    public string ContactName { get; set; } = string.Empty;

    [Phone]
    public string ContactPhone { get; set; } = string.Empty;

    [EmailAddress]
    public string ContactEmail { get; set; } = string.Empty;

    [Range(1, 10)]
    public int Priority { get; set; } = 5;

    [Range(25, 1000)]
    public int GeofenceRadius { get; set; } = 100;

    public string ServiceType { get; set; } = "both";

    [StringLength(2000)]
    public string Notes { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class WinterWatchInviteInput
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "client";

    public bool CreateEmployee { get; set; }

    public string EmployeeCategory { get; set; } = "both";
}

public sealed class WinterWatchConnectionInput
{
    [Required, Url]
    public string FunctionUrl { get; set; } = string.Empty;

    [Required, MinLength(24)]
    public string IntegrationSecret { get; set; } = string.Empty;

    [Required, Url]
    public string InviteRedirectUrl { get; set; } =
        "https://winterwatch-pro.info/auth/callback";
}
