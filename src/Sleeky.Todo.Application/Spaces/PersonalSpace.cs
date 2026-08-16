using System.Security.Cryptography;
using System.Text;

namespace Sleeky.Todo.Application.Spaces;

/// <summary>
/// The Space every user starts with.
/// </summary>
/// <remarks>
/// Nothing marks it as personal once it exists: it is an ordinary Space that
/// can be renamed and shared. What makes it special is only how it is
/// created — its identifier is derived from the user, so "ensure the user has
/// one" is an idempotent insert rather than a check-then-create that two
/// first requests could both pass.
/// </remarks>
public static class PersonalSpace
{
    public const string Name = "My Space";

    private const int GuidByteCount = 16;

    /// <summary>
    /// The length of a <c>D</c>-formatted identifier, which is the name the
    /// hash is taken over: the canonical lowercase text, so the derivation
    /// does not depend on any platform's byte layout.
    /// </summary>
    private const int GuidTextLength = 36;
    private const byte VersionByteIndex = 6;
    private const byte VariantByteIndex = 8;

    /// <summary>
    /// The namespace under which personal Space identifiers are derived. A
    /// fixed constant: changing it would give every user a new personal
    /// Space and orphan the old one.
    /// </summary>
    private static readonly Guid Namespace = Guid.Parse("6f0d0e5a-2b6b-4c7f-9a3e-8f1c2d4b6e70");

    /// <summary>
    /// The identifier of <paramref name="userId"/>'s personal Space: an
    /// RFC 4122 version 5 (SHA-1, name-based) UUID of the user identifier
    /// under <see cref="Namespace"/>. Deterministic, so every caller derives
    /// the same value without coordinating.
    /// </summary>
    public static Guid IdFor(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user identifier is required.", nameof(userId));
        }

        Span<byte> input = stackalloc byte[GuidByteCount + GuidTextLength];
        Namespace.TryWriteBytes(input, bigEndian: true, out int _);
        Encoding.ASCII.GetBytes(userId.ToString("D"), input[GuidByteCount..]);

        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(input, hash);

        Span<byte> result = hash[..GuidByteCount];
        result[VersionByteIndex] = (byte)((result[VersionByteIndex] & 0x0F) | 0x50);
        result[VariantByteIndex] = (byte)((result[VariantByteIndex] & 0x3F) | 0x80);

        return new Guid(result, bigEndian: true);
    }
}
