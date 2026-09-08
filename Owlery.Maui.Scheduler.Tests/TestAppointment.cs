namespace Owlery.Maui.Scheduler.Tests;

internal sealed record TestAppointment(DateTime Start, DateTime End, string? Subject = null, object? Identity = null) : ISchedulerAppointment
{
    // Derived from the values so that re-creating "the same" appointment — which is what a host does
    // when it reloads — produces a matching key. A move is the one change that must not: see MovedTo.
    public object Key => Identity ?? $"{Subject}|{Start:O}|{End:O}";

    public static TestAppointment At(DateTime day, string time, double hours, string? subject = null)
    {
        var start = day.Date.Add(TimeSpan.Parse(time));
        return new TestAppointment(start, start.AddHours(hours), subject);
    }

    /// <summary>The same appointment starting somewhere else, which is what a host applies a drop as.</summary>
    /// <remarks>
    /// It keeps the key it had, as the contract asks: an appointment that changed its identity by
    /// moving would read as one disappearing and another arriving.
    /// </remarks>
    public TestAppointment MovedTo(DateTime start) =>
        this with { Start = start, End = start + (End - Start), Identity = Key };
}
