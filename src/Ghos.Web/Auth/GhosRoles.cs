namespace Ghos.Web.Auth;

public static class GhosRoles
{
    public const string Administrator = "Administrator";
    public const string Manager = "Manager";
    public const string Operations = "Operations";
    public const string Marketing = "Marketing";

    public static readonly string[] All =
    [
        Administrator,
        Manager,
        Operations,
        Marketing
    ];
}

public static class GhosPolicies
{
    public const string AdministratorOnly = "AdministratorOnly";
    public const string Management = "Management";
    public const string Operations = "Operations";
    public const string Marketing = "Marketing";
    public const string Assets = "Assets";
}
