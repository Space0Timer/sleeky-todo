namespace Sleeky.Todo.Domain.Enums;

/// <summary>
/// The kind of principal a Space access entry names.
/// </summary>
/// <remarks>
/// Only users exist today. The discriminator is stored beside the subject
/// identifier so a second kind can be added later without reshaping the
/// access list; nothing branches on it yet.
/// </remarks>
public enum SubjectType
{
    User = 1,
}
