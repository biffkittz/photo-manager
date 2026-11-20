using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PhotoManager.Api.Audit;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Data;
using PhotoManager.Data.Entities;

namespace PhotoManager.Api.Members;

/// <summary>
/// Family members invited from the People page: invites, removal, and the Google sign-in rules. A member is
/// recognized only by the Google account's permanent id (<c>sub</c>), bound once, when the invite is accepted;
/// email addresses are never used to grant access. Members are always plain members: admins exist only in
/// configuration (<see cref="AuthOptions.Users"/>).
/// </summary>
/// <param name="db">Database context (request scoped; shared with <paramref name="audit"/>).</param>
/// <param name="audit">Audit trail.</param>
/// <param name="time">Clock.</param>
public sealed class MemberService(PhotoDbContext db, AuditLog audit, TimeProvider time)
{
    /// <summary>How long an invite link works.</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    /// <summary>Lists everyone invited so far: current people first, then removed ones.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The members.</returns>
    public async Task<IReadOnlyList<MemberResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var members = await db.Members.AsNoTracking().ToListAsync(cancellationToken);
        return [.. members
            .OrderBy(m => m.RemovedAt is not null)
            .ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => ToResponse(m, now))];
    }

    /// <summary>Invites someone and returns the one-time link token.</summary>
    /// <param name="name">Validated display name.</param>
    /// <param name="email">Validated, lower-case Google address the invite is limited to, or null.</param>
    /// <param name="admin">The admin sending the invite.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The invite.</returns>
    public async Task<InviteCreated> InviteAsync(string name, string? email, ClaimsPrincipal admin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admin);
        var member = new FamilyMember { DisplayName = name, InviteEmail = email };
        var token = IssueInvite(member, admin);
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("member.invite", Target(member), new { name, limitedToEmail = email is not null });
        return new InviteCreated(ToResponse(member, time.GetUtcNow()), token, member.InviteExpiresAt!.Value);
    }

    /// <summary>
    /// Issues a new invite link for someone who does not currently have access (invite pending or expired, or
    /// removed). Any earlier link stops working, and so does any Google account linked before: the person links
    /// one again by accepting the new link.
    /// </summary>
    /// <param name="id">Member id.</param>
    /// <param name="admin">The admin sending the invite.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The invite; null when there is no such member; <c>Token</c> empty when the member is active.</returns>
    public async Task<InviteCreated?> RenewInviteAsync(long id, ClaimsPrincipal admin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admin);
        var member = await db.Members.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (StatusOf(member, now) == MemberStatus.Active)
        {
            return new InviteCreated(ToResponse(member, now), string.Empty, now);
        }

        var token = IssueInvite(member, admin);
        member.GoogleSubject = null;
        member.RemovedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("member.invite.renew", Target(member), new { name = member.DisplayName });
        return new InviteCreated(ToResponse(member, now), token, member.InviteExpiresAt!.Value);
    }

    /// <summary>Removes someone's access (or cancels their invite). Their session ends with their next request.</summary>
    /// <param name="id">Member id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The member, or null when there is no such member.</returns>
    public async Task<MemberResponse?> RemoveAsync(long id, CancellationToken cancellationToken)
    {
        var member = await db.Members.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (member.RemovedAt is null)
        {
            member.RemovedAt = now;
            member.InviteTokenHash = null;
            member.InviteExpiresAt = null;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync("member.remove", Target(member), new { name = member.DisplayName });
        }

        return ToResponse(member, now);
    }

    /// <summary>Finds the person a still-valid invite link belongs to.</summary>
    /// <param name="token">Untrusted token from the link.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The member, or null when the link is not (or no longer) valid.</returns>
    public async Task<FamilyMember?> FindPendingInviteAsync(string? token, CancellationToken cancellationToken)
    {
        if (!InviteTokens.IsWellFormed(token))
        {
            return null;
        }

        var hash = InviteTokens.Hash(token!);
        var now = time.GetUtcNow();
        return await db.Members.AsNoTracking()
            .SingleOrDefaultAsync(m => m.InviteTokenHash == hash && m.InviteExpiresAt > now && m.RemovedAt == null, cancellationToken);
    }

    /// <summary>
    /// Decides a Google sign-in. With an invite, links the Google account to the invited person (once; the invite
    /// is then used up). Without one, signs in the current member linked to that Google account, if any.
    /// </summary>
    /// <param name="account">The Google account.</param>
    /// <param name="invite">The invite being accepted, or null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The outcome.</returns>
    internal async Task<GoogleSignInResult> SignInWithGoogleAsync(GoogleAccount account, PendingInvite? invite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var now = time.GetUtcNow();
        var email = account.Email is null ? null : Truncate(account.Email, 320);
        if (invite is null)
        {
            var member = await db.Members.SingleOrDefaultAsync(m => m.GoogleSubject == account.Subject && m.RemovedAt == null, cancellationToken);
            if (member is null)
            {
                await audit.WriteAsync("auth.denied", null, new { provider = SignInProviders.Google, email }, actorOverride: email ?? "unknown Google account");
                return new GoogleSignInResult(GoogleSignInOutcome.NotInvited, null);
            }

            member.LastSignInAt = now;
            member.GoogleEmail = email;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync("auth.signin", null, new { provider = SignInProviders.Google }, actorOverride: member.DisplayName, actorIdOverride: member.Id);
            return new GoogleSignInResult(GoogleSignInOutcome.SignedIn, member);
        }

        var invited = await db.Members.SingleOrDefaultAsync(m => m.Id == invite.MemberId, cancellationToken);
        if (invited is null
            || invited.RemovedAt is not null
            || !string.Equals(invited.InviteTokenHash, invite.TokenHash, StringComparison.Ordinal)
            || invited.InviteExpiresAt is not { } expires
            || expires <= now)
        {
            return await RefuseInviteAsync(GoogleSignInOutcome.InviteInvalid, invite.MemberId, email);
        }

        if (invited.InviteEmail is { } required
            && !(account.EmailVerified && string.Equals(email, required, StringComparison.OrdinalIgnoreCase)))
        {
            return await RefuseInviteAsync(GoogleSignInOutcome.WrongAccount, invited.Id, email);
        }

        if (await db.Members.AnyAsync(m => m.GoogleSubject == account.Subject && m.RemovedAt == null && m.Id != invited.Id, cancellationToken))
        {
            return await RefuseInviteAsync(GoogleSignInOutcome.AccountInUse, invited.Id, email);
        }

        invited.GoogleSubject = account.Subject;
        invited.GoogleEmail = email;
        invited.InviteTokenHash = null;
        invited.InviteExpiresAt = null;
        invited.JoinedAt = now;
        invited.LastSignInAt = now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // An admin renewed or removed the invite while the person was at Google.
            return await RefuseInviteAsync(GoogleSignInOutcome.InviteInvalid, invited.Id, email);
        }
        catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception, DbErrors.UniqueGoogleAccountIndex))
        {
            // The same Google account accepted another invite at the same moment.
            return await RefuseInviteAsync(GoogleSignInOutcome.AccountInUse, invited.Id, email);
        }

        await audit.WriteAsync("member.join", Target(invited), new { name = invited.DisplayName, email }, actorOverride: invited.DisplayName, actorIdOverride: invited.Id);
        return new GoogleSignInResult(GoogleSignInOutcome.SignedIn, invited);
    }

    /// <summary>Whether a member's session is still allowed: not removed, and still linked to the same Google account.</summary>
    /// <param name="memberId">Member id from the session.</param>
    /// <param name="googleSubject">Google account from the session.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Whether the session may continue.</returns>
    public Task<bool> IsActiveAsync(long memberId, string googleSubject, CancellationToken cancellationToken) =>
        db.Members.AsNoTracking().AnyAsync(m => m.Id == memberId && m.RemovedAt == null && m.GoogleSubject == googleSubject, cancellationToken);

    private async Task<GoogleSignInResult> RefuseInviteAsync(GoogleSignInOutcome outcome, long memberId, string? email)
    {
        // Nothing of a refused sign-in may be saved by the audit write below, which shares this context.
        db.ChangeTracker.Clear();
        await audit.WriteAsync("member.join.denied", memberId.ToString(CultureInfo.InvariantCulture), new { reason = outcome.ToString(), email }, actorOverride: email ?? "unknown Google account");
        return new GoogleSignInResult(outcome, null);
    }

    private string IssueInvite(FamilyMember member, ClaimsPrincipal admin)
    {
        var token = InviteTokens.Create();
        member.InviteTokenHash = InviteTokens.Hash(token);
        member.InviteExpiresAt = time.GetUtcNow() + InviteLifetime;
        member.InvitedById = admin.UserId();
        member.InvitedBy = Truncate(admin.Login(), 100);
        return token;
    }

    private static string StatusOf(FamilyMember member, DateTimeOffset now) =>
        member.RemovedAt is not null ? MemberStatus.Removed
        : member.GoogleSubject is not null ? MemberStatus.Active
        : member.InviteExpiresAt > now ? MemberStatus.Invited
        : MemberStatus.Expired;

    private static MemberResponse ToResponse(FamilyMember member, DateTimeOffset now) => new(
        member.Id,
        member.DisplayName,
        StatusOf(member, now),
        member.GoogleEmail,
        member.InviteEmail,
        member.InvitedBy,
        member.InviteExpiresAt,
        member.JoinedAt,
        member.LastSignInAt,
        member.RemovedAt);

    private static string Target(FamilyMember member) => member.Id.ToString(CultureInfo.InvariantCulture);

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
