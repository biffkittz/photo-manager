using PhotoManager.Data.Entities;

namespace PhotoManager.Api.Members;

/// <summary>Result of <see cref="MemberService.SignInWithGoogleAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Member">The member who signed in, when <see cref="GoogleSignInOutcome.SignedIn"/>.</param>
public sealed record GoogleSignInResult(GoogleSignInOutcome Outcome, FamilyMember? Member);
