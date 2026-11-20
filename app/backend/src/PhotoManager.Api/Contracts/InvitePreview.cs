namespace PhotoManager.Api.Contracts;

/// <summary>What the invite page shows before the person signs in with Google.</summary>
/// <param name="Name">The invited person's name.</param>
/// <param name="InvitedBy">Who invited them.</param>
/// <param name="ExpiresAt">When the link stops working.</param>
/// <param name="ForSpecificAccount">Whether only one Google address may accept it (the address is not revealed).</param>
public sealed record InvitePreview(string Name, string InvitedBy, DateTimeOffset ExpiresAt, bool ForSpecificAccount);
