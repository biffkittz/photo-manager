using System.Net;

namespace PhotoManager.Api.Tests.Support;

/// <summary>
/// Stands in for Google's token and userinfo endpoints (the OAuth handler's back channel), so tests run the real
/// Google handler, cookies and membership rules end to end. <see cref="Account"/> is the Google account that "signs in".
/// </summary>
public sealed class FakeGoogle : HttpMessageHandler
{
    public GoogleUser Account { get; set; } = new("google-sub-1", "someone@example.com", true);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        var response = url switch
        {
            "https://oauth2.googleapis.com/token" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { access_token = "fake-access-token", token_type = "Bearer", expires_in = 3600 }),
            },
            "https://www.googleapis.com/oauth2/v3/userinfo" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { sub = Account.Subject, email = Account.Email, email_verified = Account.EmailVerified }),
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        return Task.FromResult(response);
    }
}
