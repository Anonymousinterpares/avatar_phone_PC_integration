using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Bluey.Auth;

public sealed class Pkce
{
    public required string Verifier { get; init; }
    public required string Challenge { get; init; }
    public required string State { get; init; }
    public required string Nonce { get; init; }

    public static Pkce Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new Pkce
        {
            Verifier = verifier,
            Challenge = challenge,
            State = Base64Url(RandomNumberGenerator.GetBytes(16)),
            Nonce = Base64Url(RandomNumberGenerator.GetBytes(16)),
        };
    }

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class AuthorizeStart
{
    public required string Url { get; init; }
    public required string RedirectUri { get; init; }
    public required Pkce Pkce { get; init; }
    public required string ClientId { get; init; }
}

public static class ChatGptOAuth
{
    public const string AuthorizeEndpoint = "https://auth.openai.com/api/accounts/authorize";
    public const string TokenEndpoint = "https://auth.openai.com/api/accounts/oauth/token";
    public const string Resource = "https://api.openai.com/v1";
    public const string Issuer = "https://auth.openai.com";
    public const string AgentName = "Bluey PC";
    public const string PlanScope = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";

    public static AuthorizeStart Begin(AppSettings settings, int port, bool returning)
    {
        var pkce = Pkce.Create();
        var redirect = $"http://127.0.0.1:{port}/auth/callback";
        var clientId = returning && !string.IsNullOrEmpty(settings.ChatGpt?.ClientId)
            ? settings.ChatGpt!.ClientId
            : "dynamic_agent_client";
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirect,
            ["scope"] = PlanScope,
            ["resource"] = Resource,
            ["state"] = pkce.State,
            ["nonce"] = pkce.Nonce,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = pkce.Challenge,
            ["ext_agent_host_id"] = settings.HostId,
        };
        if (clientId == "dynamic_agent_client") query["agent_name_hint"] = AgentName;
        else
        {
            if (!string.IsNullOrEmpty(settings.ChatGpt?.IdToken)) query["id_token_hint"] = settings.ChatGpt!.IdToken;
            if (!string.IsNullOrEmpty(settings.ChatGpt?.Email)) query["login_hint"] = settings.ChatGpt!.Email;
        }
        var url = AuthorizeEndpoint + "?" + string.Join("&", query.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new AuthorizeStart { Url = url, RedirectUri = redirect, Pkce = pkce, ClientId = clientId };
    }

    public static async Task<ChatGptAccount> ExchangeAsync(HttpClient http, AuthorizeStart start, string code, string? issuedClientId, string jwksJson, CancellationToken cancellationToken)
    {
        var clientId = string.IsNullOrEmpty(issuedClientId) ? start.ClientId : issuedClientId;
        if (clientId == "dynamic_agent_client")
            throw new InvalidOperationException("ChatGPT did not return an issued client id. Registration did not finish.");
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = start.Pkce.Verifier,
            ["redirect_uri"] = start.RedirectUri,
            ["resource"] = Resource,
        };
        var token = await PostFormAsync(http, TokenEndpoint, body, cancellationToken);
        return AccountFromToken(token, clientId, jwksJson, start.Pkce.Nonce, expectedSubject: null);
    }

    public static async Task<ChatGptAccount> RefreshAsync(HttpClient http, ChatGptAccount current, string jwksJson, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = current.ClientId,
            ["refresh_token"] = current.RefreshToken,
            ["resource"] = Resource,
        };
        var token = await PostFormAsync(http, TokenEndpoint, body, cancellationToken);
        var account = AccountFromToken(token, current.ClientId, jwksJson, expectedNonce: null, expectedSubject: current.Subject);
        if (string.IsNullOrEmpty(account.RefreshToken)) account.RefreshToken = current.RefreshToken;
        return account;
    }

    public static ChatGptAccount AccountFromToken(JsonElement token, string clientId, string jwksJson, string? expectedNonce, string? expectedSubject)
    {
        var access = Required(token, "access_token");
        var idToken = Required(token, "id_token");
        var identity = ValidateIdToken(idToken, clientId, jwksJson, expectedNonce);
        if (expectedSubject != null && !string.Equals(identity.Subject, expectedSubject, StringComparison.Ordinal))
            throw new InvalidOperationException("ChatGPT returned a different account than the one selected.");
        var scopes = (token.TryGetProperty("scope", out var scope) ? scope.GetString() : "")?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var expiresIn = token.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds) ? seconds : 3600;
        return new ChatGptAccount
        {
            Email = identity.Email,
            Subject = identity.Subject,
            Issuer = Issuer,
            ClientId = clientId,
            IdToken = idToken,
            AccessToken = access,
            RefreshToken = token.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() ?? "" : "",
            Scopes = scopes.ToList(),
            AccessExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60),
            SavedAt = DateTimeOffset.UtcNow,
        };
    }

    public static (string Subject, string Email) ValidateIdToken(string idToken, string clientId, string jwksJson, string? expectedNonce)
    {
        var keys = new JsonWebKeySet(jwksJson);
        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = clientId,
            ValidateLifetime = true,
            IssuerSigningKeys = keys.GetSigningKeys(),
            NameClaimType = "email",
        };
        var principal = handler.ValidateToken(idToken, parameters, out var validated);
        var jwt = (JwtSecurityToken)validated;
        if (expectedNonce != null)
        {
            var nonce = jwt.Claims.FirstOrDefault(claim => claim.Type == "nonce")?.Value;
            if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
                throw new InvalidOperationException("ChatGPT sign-in nonce did not match.");
        }
        var subject = principal.FindFirst("sub")?.Value ?? jwt.Subject;
        if (string.IsNullOrEmpty(subject)) throw new InvalidOperationException("ChatGPT ID token has no subject.");
        var email = jwt.Claims.FirstOrDefault(claim => claim.Type == "email")?.Value ?? "";
        return (subject, email);
    }

    public static async Task<string> FetchJwksAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var discovery = await http.GetAsync("https://auth.openai.com/.well-known/openid-configuration", cancellationToken);
        var discoveryJson = await discovery.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(discoveryJson);
        var jwksUri = doc.RootElement.GetProperty("jwks_uri").GetString()
            ?? throw new InvalidOperationException("OpenAI discovery document has no jwks_uri.");
        return await http.GetStringAsync(jwksUri, cancellationToken);
    }

    private static async Task<JsonElement> PostFormAsync(HttpClient http, string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ChatGPT token request failed ({(int)response.StatusCode}).");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string Required(JsonElement token, string name)
    {
        if (!token.TryGetProperty(name, out var value) || string.IsNullOrEmpty(value.GetString()))
            throw new InvalidOperationException($"ChatGPT token response has no {name}.");
        return value.GetString()!;
    }
}
