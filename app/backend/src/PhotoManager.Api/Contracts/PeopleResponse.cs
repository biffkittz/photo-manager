namespace PhotoManager.Api.Contracts;

/// <summary>Everyone who can use Photo Manager, for the People page.</summary>
/// <param name="Members">People invited from the app (Google sign-in).</param>
/// <param name="GitHubAccounts">Accounts managed in Terraform (GitHub sign-in), including the admins.</param>
/// <param name="GoogleSignIn">Whether Google sign-in is configured; without it nobody can be invited.</param>
public sealed record PeopleResponse(IReadOnlyList<MemberResponse> Members, IReadOnlyList<ConfiguredAccount> GitHubAccounts, bool GoogleSignIn);
