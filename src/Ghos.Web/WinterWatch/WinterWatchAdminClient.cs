using System.Net.Http.Json;
using System.Text.Json;

namespace Ghos.Web.WinterWatch;

public sealed class WinterWatchAdminClient(
    HttpClient httpClient,
    WinterWatchCredentialStore credentialStore)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public Task<WinterWatchAdminResponse> GetOrganizationsAsync(
        string actor,
        CancellationToken cancellationToken = default) =>
        SendAsync("list_organizations", new { }, actor, cancellationToken);

    public Task<WinterWatchAdminResponse> GetWorkspaceAsync(
        Guid organizationId,
        string actor,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "get_workspace",
            new { organization_id = organizationId },
            actor,
            cancellationToken);

    public Task<WinterWatchAdminResponse> OnboardAsync(
        WinterWatchOnboardingInput input,
        string actor,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "onboard",
            new
            {
                organization = new
                {
                    name = input.OrganizationName,
                    slug = input.Slug,
                    plan = input.Plan,
                    status = input.Status
                },
                primary_contact = new
                {
                    email = input.ContactEmail,
                    full_name = input.ContactName,
                    phone = input.ContactPhone,
                    role = input.ContactRole,
                    create_employee = input.CreateEmployee,
                    employee_category = input.EmployeeCategory
                },
                accounts = input.AddFirstAccount
                    ? new[] { ToAccountPayload(input.Account) }
                    : Array.Empty<object>()
            },
            actor,
            cancellationToken);

    public Task<WinterWatchAdminResponse> UpdateOrganizationAsync(
        WinterWatchOrganizationSummary organization,
        string actor,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "update_organization",
            new
            {
                organization_id = organization.Id,
                name = organization.Name,
                slug = organization.Slug,
                plan = organization.Plan,
                status = organization.Status
            },
            actor,
            cancellationToken);

    public Task<WinterWatchAdminResponse> SaveAccountAsync(
        Guid organizationId,
        WinterWatchAccountInput input,
        string actor,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "save_account",
            new
            {
                organization_id = organizationId,
                account = ToAccountPayload(input)
            },
            actor,
            cancellationToken);

    public Task<WinterWatchAdminResponse> InviteUserAsync(
        Guid organizationId,
        WinterWatchInviteInput input,
        string actor,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "invite_user",
            new
            {
                organization_id = organizationId,
                full_name = input.FullName,
                email = input.Email,
                phone = input.Phone,
                role = input.Role,
                create_employee = input.CreateEmployee,
                employee_category = input.EmployeeCategory
            },
            actor,
            cancellationToken);

    public Task<WinterWatchAdminResponse> UpdateUserAccessAsync(
        Guid organizationId,
        WinterWatchWorkspaceUser user,
        bool hasAccess,
        string actor,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            "update_user_access",
            new
            {
                organization_id = organizationId,
                user_id = user.Id,
                full_name = user.FullName,
                phone = user.Phone,
                role = user.Role,
                has_access = hasAccess
            },
            actor,
            cancellationToken);

    public async Task ValidateAsync(
        string functionUrl,
        string secret,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var credentials = new WinterWatchCredentials(
            WinterWatchCredentialStore.NormalizeFunctionUrl(functionUrl),
            secret.Trim(),
            "https://winterwatch-pro.info/auth/callback");
        await SendWithCredentialsAsync(
            credentials,
            "health",
            new { },
            actor,
            cancellationToken);
    }

    private async Task<WinterWatchAdminResponse> SendAsync(
        string action,
        object payload,
        string actor,
        CancellationToken cancellationToken)
    {
        var credentials = await credentialStore.GetAsync(cancellationToken)
            ?? throw new WinterWatchConnectionException(
                "Configure the WinterWatch admin connection first.");

        return await SendWithCredentialsAsync(
            credentials,
            action,
            payload,
            actor,
            cancellationToken);
    }

    private async Task<WinterWatchAdminResponse> SendWithCredentialsAsync(
        WinterWatchCredentials credentials,
        string action,
        object payload,
        string actor,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            credentials.FunctionUrl);
        request.Headers.TryAddWithoutValidation(
            "x-ghos-integration-secret",
            credentials.IntegrationSecret);
        request.Headers.TryAddWithoutValidation("x-ghos-actor", actor);
        request.Content = JsonContent.Create(
            new
            {
                action,
                payload,
                invite_redirect_to = credentials.InviteRedirectUrl
            },
            options: JsonOptions);

        using var response = await httpClient.SendAsync(
            request,
            cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<WinterWatchAdminResponse>(
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode || result is null || !result.Success)
        {
            var message = result?.Error;
            if (string.IsNullOrWhiteSpace(message))
            {
                message =
                    $"WinterWatch returned {(int)response.StatusCode} {response.ReasonPhrase}.";
            }

            throw new WinterWatchConnectionException(message);
        }

        return result;
    }

    private static object ToAccountPayload(WinterWatchAccountInput input) => new
    {
        id = input.Id,
        name = input.Name,
        address = input.Address,
        city = input.City,
        state = input.State,
        zip = input.Zip,
        contact_name = input.ContactName,
        contact_phone = input.ContactPhone,
        contact_email = input.ContactEmail,
        priority = input.Priority,
        geofence_radius = input.GeofenceRadius,
        service_type = input.ServiceType,
        notes = input.Notes,
        is_active = input.IsActive
    };
}
