namespace Sleeky.Todo.Api.Contracts.Auth;

public sealed class AntiforgeryTokenResponse
{
    public AntiforgeryTokenResponse(string token, string headerName)
    {
        Token = token;
        HeaderName = headerName;
    }

    public string Token { get; }

    public string HeaderName { get; }
}
