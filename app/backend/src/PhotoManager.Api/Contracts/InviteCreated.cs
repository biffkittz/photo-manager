namespace PhotoManager.Api.Contracts;

/// <summary>A new invite link. The token is returned only here, once; the server keeps just its hash.</summary>
/// <param name="Member">The invited person.</param>
/// <param name="Token">The secret part of the link (<c>/invite#&lt;token&gt;</c>).</param>
/// <param name="ExpiresAt">When the link stops working.</param>
public sealed record InviteCreated(MemberResponse Member, string Token, DateTimeOffset ExpiresAt);
