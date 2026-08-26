namespace Owlery.Maui.Scheduler.Tests;

internal sealed record TestAppointment(DateTime Start, DateTime End, string? Subject = null) : ISchedulerAppointment
{
    // Derived from the values so that re-creating "the same" appointment — which is what a host does
    // when it reloads — produces a matching key.
    public object Key => $"{Subject}|{Start:O}|{End:O}";

    public static TestAppointment At(DateTime day, string time, double hours, string? subject = null)
    {
        var start = day.Date.Add(TimeSpan.Parse(time));
        return new TestAppointment(start, start.AddHours(hours), subject);
    }
}
