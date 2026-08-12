using Sleeky.Todo.Application.Abstractions.Time;

namespace Sleeky.Todo.Infrastructure.Time;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
