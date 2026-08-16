using Sleeky.Todo.Domain.Entities;
using Sleeky.Todo.Domain.ValueObjects;

namespace Sleeky.Todo.Infrastructure.Persistence.Documents;

internal static class SpaceDocumentMapper
{
    public static SpaceDocument FromDomain(Space space, long? version = null)
    {
        ArgumentNullException.ThrowIfNull(space);

        return new SpaceDocument
        {
            Id = space.Id,
            Name = space.Name,
            Access = space.Access
                .Select(entry => new SpaceAccessEntryDocument
                {
                    SubjectId = entry.SubjectId,
                    SubjectType = entry.SubjectType,
                    Permission = entry.Permission,
                })
                .ToList(),
            Version = version ?? space.Version,
            CreatedAt = space.CreatedAt.UtcDateTime,
            UpdatedAt = space.UpdatedAt.UtcDateTime,
        };
    }

    public static Space ToDomain(SpaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Space.Rehydrate(
            document.Id,
            document.Name,
            document.Access.Select(entry => new SpaceAccessEntry(
                entry.SubjectId,
                entry.SubjectType,
                entry.Permission)),
            document.Version,
            ToDateTimeOffset(document.CreatedAt),
            ToDateTimeOffset(document.UpdatedAt));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
