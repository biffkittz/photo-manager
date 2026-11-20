using System.Globalization;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Security;

namespace PhotoManager.Api.Members;

/// <summary>The People page (admin only): invite family members, send a new link, remove access.</summary>
public static class MemberEndpoints
{
    private const int MaxNameLength = 100;
    private const int MaxEmailLength = 320;

    /// <summary>Maps <c>/api/members/*</c>.</summary>
    /// <param name="api">The /api route group.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapMemberEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var members = api.MapGroup("/members").RequireAuthorization(PhotoRoles.AdminPolicy);
        members.MapGet("/", ListAsync);
        members.MapPost("/", InviteAsync).RequireRateLimiting(RateLimitPolicies.Expensive);
        members.MapPost("/{id:long}/invite", RenewInviteAsync).RequireRateLimiting(RateLimitPolicies.Expensive);
        members.MapPost("/{id:long}/remove", RemoveAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(MemberService members, IOptionsMonitor<AuthOptions> auth, CancellationToken ct)
    {
        var settings = auth.CurrentValue;
        var gitHub = settings.Users
            .Where(u => u.GitHubId > 0)
            .Select(u => new ConfiguredAccount(u.Login ?? u.GitHubId.ToString(CultureInfo.InvariantCulture), u.Role))
            .ToList();
        return TypedResults.Ok(new PeopleResponse(await members.ListAsync(ct), gitHub, settings.GoogleSignInEnabled));
    }

    private static async Task<IResult> InviteAsync(InviteRequest request, HttpContext http, MemberService members, IOptionsMonitor<AuthOptions> auth, CancellationToken ct)
    {
        if (!auth.CurrentValue.GoogleSignInEnabled)
        {
            return GoogleNotConfigured();
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaxNameLength || name.Any(char.IsControl))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [$"Enter a name of 1 to {MaxNameLength} characters."] });
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            email = request.Email.Trim().ToLowerInvariant();
            if (email.Length > MaxEmailLength || !MailAddress.TryCreate(email, out var parsed) || !string.Equals(parsed.Address, email, StringComparison.Ordinal))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter an email address like name@gmail.com, or leave it empty."] });
            }
        }

        return TypedResults.Ok(await members.InviteAsync(name, email, http.User, ct));
    }

    private static async Task<IResult> RenewInviteAsync(long id, HttpContext http, MemberService members, IOptionsMonitor<AuthOptions> auth, CancellationToken ct)
    {
        if (!auth.CurrentValue.GoogleSignInEnabled)
        {
            return GoogleNotConfigured();
        }

        var invite = await members.RenewInviteAsync(id, http.User, ct);
        if (invite is null)
        {
            return TypedResults.NotFound();
        }

        return invite.Token.Length == 0
            ? TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: $"{invite.Member.Name} already has access. Remove them first to link a different Google account.")
            : TypedResults.Ok(invite);
    }

    private static async Task<IResult> RemoveAsync(long id, MemberService members, CancellationToken ct) =>
        await members.RemoveAsync(id, ct) is { } member ? TypedResults.Ok(member) : TypedResults.NotFound();

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult GoogleNotConfigured() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Google sign-in is not set up yet, so nobody can accept an invite. See \"Inviting family members\" in docs/operations.md.");
}
