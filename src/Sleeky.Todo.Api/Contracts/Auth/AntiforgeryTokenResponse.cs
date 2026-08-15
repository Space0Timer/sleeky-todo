namespace Sleeky.Todo.Api.Contracts.Auth;

public sealed class AntiforgeryTokenResponse
{
    public AntiforgeryTokenResponse(
        string token,
        string headerName,
        string formFieldName)
    {
        Token = token;
        HeaderName = headerName;
        FormFieldName = formFieldName;
    }

    public string Token { get; }

    public string HeaderName { get; }

    /// <summary>
    /// Gets the field name a form post must use to carry the token. Validation
    /// reads the form field before the header whenever a request has a form
    /// content type, which is how the sign-out navigation stays protected: a
    /// browser-owned form post cannot set a request header.
    /// </summary>
    public string FormFieldName { get; }
}
