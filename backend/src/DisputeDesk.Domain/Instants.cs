namespace DisputeDesk.Domain;

/// <summary>
/// Pure functions on instants. They read no clock: each use case reads <see cref="TimeProvider"/> once and passes the
/// instant in (AD-4).
/// </summary>
public static class Instants
{
    /// <summary>
    /// The instant cut to the whole microseconds a Postgres <c>timestamptz</c> keeps (PostgreSQL, Date/Time Types); a
    /// <see cref="DateTimeOffset"/> has 100 ns ticks.
    /// </summary>
    public static DateTimeOffset ToWholeMicroseconds(DateTimeOffset instant) =>
        instant.AddTicks(-(instant.Ticks % TimeSpan.TicksPerMicrosecond));

    /// <summary>The later of the two instants.</summary>
    public static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;
}
