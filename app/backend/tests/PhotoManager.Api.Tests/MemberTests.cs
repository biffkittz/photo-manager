using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PhotoManager.Api.Auth;
using PhotoManager.Api.Contracts;
using PhotoManager.Api.Members;
using PhotoManager.Api.Tests.Support;
using PhotoManager.Core.Domain;
using PhotoManager.Data.Entities;

namespace PhotoManager.Api.Tests;

/// <summary>
/// Family members invited from the People page, signing in with Google. These tests run the real Google OAuth
/// handler, session cookies and database rules (as the web role); only Google's servers are faked
/// (<see cref="FakeGoogle"/>). Admins act through the header-based test scheme.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MemberTests(ApiFactory factory)
{
    private const string TestOrCookie = "test-or-cookie";

    // Fixture rows inserted with explicit ids, far below anything the id sequence (-1, -2, ...) hands out.
    private const long FixtureMemberId = -9_000_000_001;
    private const long KeptMemberId = -9_000_000_002;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invited_family_member_signs_in_with_google_until_removed()
    {
        var google = new FakeGoogle { Account = new GoogleUser("g-grandma", "grandma@example.com", true) };
        await using var app = WithGoogle(google);
        var admin = await AdminAsync(app);

        var invite = await InviteAsync(admin, "  Grandma  ");
        Assert.Equal("Grandma", invite.Member.Name);
        Assert.Equal(MemberStatus.Invited, invite.Member.Status);
        Assert.True(invite.Member.Id < 0, "Member ids are negative, so they never collide with GitHub ids.");
        Assert.Equal(43, invite.Token.Length);
        var stored = await factory.WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == invite.Member.Id, Ct));
        Assert.Equal(64, stored.InviteTokenHash!.Length);
        Assert.DoesNotContain(invite.Token, stored.InviteTokenHash, StringComparison.Ordinal);

        // Opening the link (the invite page posts the token from the #fragment) remembers it in a hardened cookie.
        var browser = Browser(app);
        using (var opened = await RedeemAsync(browser, invite.Token))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            var preview = await opened.Content.ReadFromJsonAsync<InvitePreview>(Json, Ct);
            Assert.Equal("Grandma", preview!.Name);
            Assert.Equal($"user{ApiFactory.AdminId}", preview.InvitedBy);
            Assert.False(preview.ForSpecificAccount);
            var cookie = opened.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-pm-invite=", StringComparison.Ordinal));
            var attributes = cookie.Split(';').Skip(1).Select(a => a.Trim()).ToList();
            Assert.Contains("httponly", attributes, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("secure", attributes, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", attributes, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("path=/", attributes, StringComparer.OrdinalIgnoreCase);
        }

        var accepted = await SignInWithGoogleAsync(browser);
        Assert.Equal("/gallery", accepted.Landing);
        Assert.Equal("select_account", accepted.Prompt);
        Assert.Equal("openid email", accepted.Scope);

        var session = await SessionAsync(browser);
        Assert.True(session.Authenticated);
        Assert.Equal(invite.Member.Id, session.User!.Id);
        Assert.Equal("Grandma", session.User.Login);
        Assert.Equal(PhotoRoles.Member, session.User.Role);
        Assert.Equal(SignInProviders.Google, session.User.Provider);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/photos", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/members", Ct)).StatusCode);

        // Her own things are hers (ownership by the negative id), and nobody else's.
        var downloadId = Guid.CreateVersion7();
        await factory.WithDbAsync(async db =>
        {
            db.Downloads.Add(new PreparedDownload { Id = downloadId, Status = DownloadStatus.Ready, PhotoIds = [Guid.NewGuid()], RequestedBy = "Grandma", RequestedById = invite.Member.Id, FileName = "x.jpg", BlobName = "x", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            return await db.SaveChangesAsync(Ct);
        });
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync($"/api/downloads/{downloadId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/downloads/{downloadId}", Ct)).StatusCode);

        // The link is used up, and the member is now recognized by the Google account id alone.
        using (var reused = await RedeemAsync(Browser(app), invite.Token))
        {
            Assert.Equal(HttpStatusCode.NotFound, reused.StatusCode);
        }

        var joined = await factory.WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == invite.Member.Id, Ct));
        Assert.Equal("g-grandma", joined.GoogleSubject);
        Assert.Equal("grandma@example.com", joined.GoogleEmail);
        Assert.Null(joined.InviteTokenHash);
        Assert.NotNull(joined.JoinedAt);
        Assert.True(await factory.WithDbAsync(db => db.AuditEvents.AnyAsync(e => e.Action == "member.join" && e.ActorId == invite.Member.Id, Ct)));

        // Later sign-ins need no invite (here: her phone), and no account chooser is forced on her.
        var phone = Browser(app);
        var again = await SignInWithGoogleAsync(phone);
        Assert.Equal("/gallery", again.Landing);
        Assert.Null(again.Prompt);
        Assert.True((await SessionAsync(phone)).Authenticated);

        // Any other Google account gets no session.
        google.Account = new GoogleUser("g-stranger", "stranger@example.com", true);
        var stranger = Browser(app);
        Assert.Equal(AuthenticationSetup.GoogleDeniedRedirect, (await SignInWithGoogleAsync(stranger)).Landing);
        Assert.False((await SessionAsync(stranger)).Authenticated);

        // Removing her ends both of her sessions at their next request, and she cannot sign in again.
        using (var removed = await admin.PostAsync($"/api/members/{invite.Member.Id}/remove", null, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            Assert.Equal(MemberStatus.Removed, (await removed.Content.ReadFromJsonAsync<MemberResponse>(Json, Ct))!.Status);
        }

        using (var afterRemoval = await browser.GetAsync("/api/photos", Ct))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, afterRemoval.StatusCode);
            Assert.Equal("\"cache\"", afterRemoval.Headers.GetValues("Clear-Site-Data").Single());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/photos", Ct)).StatusCode);
        google.Account = new GoogleUser("g-grandma", "grandma@example.com", true);
        Assert.Equal(AuthenticationSetup.GoogleDeniedRedirect, (await SignInWithGoogleAsync(Browser(app))).Landing);

        // Inviting her again unlinks the old account: only whoever accepts the new link gets in.
        using var renewed = await admin.PostAsync($"/api/members/{invite.Member.Id}/invite", null, Ct);
        var newLink = (await renewed.Content.ReadFromJsonAsync<InviteCreated>(Json, Ct))!;
        Assert.Equal(MemberStatus.Invited, newLink.Member.Status);
        Assert.Equal(AuthenticationSetup.GoogleDeniedRedirect, (await SignInWithGoogleAsync(Browser(app))).Landing);
        var newBrowser = Browser(app);
        Assert.Equal(HttpStatusCode.OK, (await RedeemAsync(newBrowser, newLink.Token)).StatusCode);
        google.Account = new GoogleUser("g-grandma-new", "grandma.new@example.com", true);
        Assert.Equal("/gallery", (await SignInWithGoogleAsync(newBrowser)).Landing);
        Assert.Equal(invite.Member.Id, (await SessionAsync(newBrowser)).User!.Id);
    }

    [Fact]
    public async Task Invites_limited_to_an_address_need_that_verified_google_account()
    {
        var google = new FakeGoogle();
        await using var app = WithGoogle(google);
        var admin = await AdminAsync(app);
        var invite = await InviteAsync(admin, "Aunt May", " May@Example.com ");
        Assert.Equal("may@example.com", invite.Member.InviteEmail);

        async Task<string?> AcceptAsAsync(GoogleUser account)
        {
            var browser = Browser(app);
            using var opened = await RedeemAsync(browser, invite.Token);
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            Assert.True((await opened.Content.ReadFromJsonAsync<InvitePreview>(Json, Ct))!.ForSpecificAccount);
            google.Account = account;
            return (await SignInWithGoogleAsync(browser)).Landing;
        }

        Assert.Equal("/login?error=invite_wrong_account", await AcceptAsAsync(new GoogleUser("g-other", "someone@example.com", true)));
        Assert.Equal("/login?error=invite_wrong_account", await AcceptAsAsync(new GoogleUser("g-may", "may@example.com", false)));
        Assert.Equal("/login?error=invite_wrong_account", await AcceptAsAsync(new GoogleUser("g-may", null, true)));
        Assert.Equal("/gallery", await AcceptAsAsync(new GoogleUser("g-may", "MAY@example.com", true)));

        var refusals = await factory.WithDbAsync(db => db.AuditEvents.CountAsync(e => e.Action == "member.join.denied" && e.Target == invite.Member.Id.ToString(CultureInfo.InvariantCulture), Ct));
        Assert.Equal(3, refusals);
    }

    [Fact]
    public async Task Invite_links_expire_are_replaced_by_newer_ones_and_link_one_account_once()
    {
        var google = new FakeGoogle { Account = new GoogleUser("g-cousin", "cousin@example.com", true) };
        await using var app = WithGoogle(google);
        var admin = await AdminAsync(app);
        var first = await InviteAsync(admin, "Cousin");

        // A new link replaces the old one.
        using var renewedResponse = await admin.PostAsync($"/api/members/{first.Member.Id}/invite", null, Ct);
        var renewed = (await renewedResponse.Content.ReadFromJsonAsync<InviteCreated>(Json, Ct))!;
        Assert.NotEqual(first.Token, renewed.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await RedeemAsync(Browser(app), first.Token)).StatusCode);

        // A link replaced while the person is at Google no longer counts when they come back.
        var browser = Browser(app);
        Assert.Equal(HttpStatusCode.OK, (await RedeemAsync(browser, renewed.Token)).StatusCode);
        using var newestResponse = await admin.PostAsync($"/api/members/{first.Member.Id}/invite", null, Ct);
        var newest = (await newestResponse.Content.ReadFromJsonAsync<InviteCreated>(Json, Ct))!;
        Assert.Equal("/login?error=invite_invalid", (await SignInWithGoogleAsync(browser)).Landing);
        Assert.False((await SessionAsync(browser)).Authenticated);

        // Expired links do not open.
        await factory.WithDbAsync(db => db.Members.Where(m => m.Id == first.Member.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.InviteExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await RedeemAsync(Browser(app), newest.Token)).StatusCode);
        var people = (await admin.GetFromJsonAsync<PeopleResponse>("/api/members", Json, Ct))!;
        Assert.True(people.GoogleSignIn);
        Assert.Equal(MemberStatus.Expired, people.Members.Single(m => m.Id == first.Member.Id).Status);
        Assert.Contains(people.GitHubAccounts, a => a.Login == ApiFactory.AdminId.ToString(CultureInfo.InvariantCulture) && a.Role == PhotoRoles.Admin);

        // One Google account belongs to at most one member.
        var personA = await InviteAsync(admin, "Person A");
        var browserA = Browser(app);
        Assert.Equal(HttpStatusCode.OK, (await RedeemAsync(browserA, personA.Token)).StatusCode);
        Assert.Equal("/gallery", (await SignInWithGoogleAsync(browserA)).Landing);
        var personB = await InviteAsync(admin, "Person B");
        var browserB = Browser(app);
        Assert.Equal(HttpStatusCode.OK, (await RedeemAsync(browserB, personB.Token)).StatusCode);
        Assert.Equal("/login?error=invite_account_in_use", (await SignInWithGoogleAsync(browserB)).Landing);

        // Someone with access cannot be sent a new link (it would unlink their account): remove them first.
        using var conflict = await admin.PostAsync($"/api/members/{personA.Member.Id}/invite", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/members/-999999/remove", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_admins_invite_and_invites_are_validated()
    {
        await using var app = WithGoogle(new FakeGoogle());
        var admin = await AdminAsync(app);
        var member = Browser(app);
        member.DefaultRequestHeaders.Add(TestAuthHandler.Header, ApiFactory.MemberId.ToString(CultureInfo.InvariantCulture));
        await ApiFactory.WithCsrfAsync(member);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/members", new InviteRequest("Someone", null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/members", Ct)).StatusCode);

        foreach (var request in new[]
        {
            new InviteRequest("   ", null),
            new InviteRequest(new string('x', 101), null),
            new InviteRequest("Bell\u0007", null),
            new InviteRequest("Someone", "not-an-email"),
            new InviteRequest("Someone", "Someone <someone@example.com>"),
        })
        {
            using var response = await admin.PostAsJsonAsync("/api/members", request, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // A token that is not even shaped like one is refused without a lookup; so is a well-formed unknown one.
        Assert.Equal(HttpStatusCode.NotFound, (await RedeemAsync(Browser(app), "short")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RedeemAsync(Browser(app), new string('A', 43))).StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_and_invites_are_off_until_google_is_configured()
    {
        var anonymous = factory.ClientFor(null);
        var session = await SessionAsync(anonymous);
        Assert.False(session.GoogleSignIn);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/auth/login/google", Ct)).StatusCode);

        var admin = await ApiFactory.WithCsrfAsync(factory.ClientFor(ApiFactory.AdminId));
        using var invite = await admin.PostAsJsonAsync("/api/members", new InviteRequest("Someone", null), Ct);
        Assert.Equal(HttpStatusCode.Conflict, invite.StatusCode);
        Assert.False((await admin.GetFromJsonAsync<PeopleResponse>("/api/members", Json, Ct))!.GoogleSignIn);
    }

    [Theory]
    [InlineData(SignInProviders.Google, FixtureMemberId, PhotoRoles.Admin, "g-1")]
    [InlineData(SignInProviders.Google, ApiFactory.MemberId, PhotoRoles.Member, "g-1")]
    [InlineData(SignInProviders.Google, FixtureMemberId, PhotoRoles.Member, null)]
    [InlineData(SignInProviders.GitHub, FixtureMemberId, PhotoRoles.Member, null)]
    [InlineData(null, FixtureMemberId, PhotoRoles.Member, null)]
    [InlineData("other", ApiFactory.MemberId, PhotoRoles.Member, null)]
    public async Task Sessions_that_do_not_fit_their_sign_in_provider_are_rejected(string? provider, long userId, string role, string? googleSubject)
    {
        await using var app = WithGoogle(new FakeGoogle());
        await using var scope = app.Services.CreateAsyncScope();

        // Even an active member's id is refused when the session claims more than a member may have.
        await factory.WithDbAsync(async db =>
        {
            if (!await db.Members.AnyAsync(m => m.Id == FixtureMemberId, Ct))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO family_members (id, display_name, google_subject, invited_by_id, invited_by, created_at, updated_at) OVERRIDING SYSTEM VALUE VALUES ({0}, 'Fixture', 'g-1', 1001, 'admin', now(), now())",
                    [FixtureMemberId],
                    Ct);
            }

            return 0;
        });

        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Role, role),
            new(PhotoClaims.SignedInAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        };
        if (provider is not null)
        {
            claims.Add(new Claim(PhotoClaims.Provider, provider));
        }

        if (googleSubject is not null)
        {
            claims.Add(new Claim(PhotoClaims.GoogleSubject, googleSubject));
        }

        var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), CookieAuthenticationDefaults.AuthenticationScheme);
        var context = new CookieValidatePrincipalContext(httpContext, scheme, options, ticket);

        await options.Events.OnValidatePrincipal(context);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task A_valid_member_session_is_kept()
    {
        await using var app = WithGoogle(new FakeGoogle());
        await factory.WithDbAsync(async db =>
        {
            if (!await db.Members.AnyAsync(m => m.Id == KeptMemberId, Ct))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO family_members (id, display_name, google_subject, invited_by_id, invited_by, created_at, updated_at) OVERRIDING SYSTEM VALUE VALUES ({0}, 'Fixture', 'g-6', 1001, 'admin', now(), now())",
                    [KeptMemberId],
                    Ct);
            }

            return 0;
        });

        await using var scope = app.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, KeptMemberId.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, PhotoRoles.Member),
            new Claim(PhotoClaims.Provider, SignInProviders.Google),
            new Claim(PhotoClaims.GoogleSubject, "g-6"),
            new Claim(PhotoClaims.SignedInAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var context = new CookieValidatePrincipalContext(
            new DefaultHttpContext { RequestServices = scope.ServiceProvider }, scheme, options, new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme));

        await options.Events.OnValidatePrincipal(context);

        Assert.Same(principal, context.Principal);
    }

    /// <summary>
    /// The API with Google configured and real cookie sessions; requests carrying the test header still act as the
    /// configured GitHub users (the admin).
    /// </summary>
    private WebApplicationFactory<Program> WithGoogle(FakeGoogle google) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Auth:GoogleClientId", "test-google-client");
        builder.UseSetting("Auth:GoogleClientSecret", "test-google-secret");
        builder.ConfigureTestServices(services =>
        {
            services.Configure<GoogleOptions>(GoogleDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = google);
            services.AddAuthentication().AddPolicyScheme(TestOrCookie, null, o => o.ForwardDefaultSelector = http =>
                http.Request.Headers.ContainsKey(TestAuthHandler.Header) ? TestAuthHandler.SchemeName : CookieAuthenticationDefaults.AuthenticationScheme);
            services.Configure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestOrCookie;
                o.DefaultAuthenticateScheme = TestOrCookie;
                o.DefaultChallengeScheme = TestOrCookie;
                o.DefaultForbidScheme = TestOrCookie;
                o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultSignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
        });
    });

    /// <summary>A browser: https (so __Host- cookies round-trip), keeps cookies, does not follow redirects.</summary>
    private static HttpClient Browser(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> app)
    {
        var client = Browser(app);
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, ApiFactory.AdminId.ToString(CultureInfo.InvariantCulture));
        return await ApiFactory.WithCsrfAsync(client);
    }

    private static async Task<InviteCreated> InviteAsync(HttpClient admin, string name, string? email = null)
    {
        using var response = await admin.PostAsJsonAsync("/api/members", new InviteRequest(name, email), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InviteCreated>(Json, Ct))!;
    }

    private static async Task<SessionResponse> SessionAsync(HttpClient browser) =>
        (await browser.GetFromJsonAsync<SessionResponse>("/api/auth/session", Json, Ct))!;

    private static async Task<HttpResponseMessage> RedeemAsync(HttpClient browser, string token)
    {
        var session = await SessionAsync(browser);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/invites/redeem") { Content = JsonContent.Create(new RedeemInviteRequest(token)) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        return await browser.SendAsync(request, Ct);
    }

    /// <summary>The browser side of a Google sign-in: start it, then come back from "Google" to the callback.</summary>
    private static async Task<GoogleRedirect> SignInWithGoogleAsync(HttpClient browser)
    {
        using var start = await browser.GetAsync("/api/auth/login/google?returnUrl=%2Fgallery", Ct);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var authorize = start.Headers.Location!;
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", authorize.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(authorize.Query);

        using var callback = await browser.GetAsync($"/signin-google?code=fake-code&state={Uri.EscapeDataString(query["state"]!)}", Ct);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return new GoogleRedirect(callback.Headers.Location?.OriginalString, query["prompt"], query["scope"]);
    }

    private sealed record GoogleRedirect(string? Landing, string? Prompt, string? Scope);
}
