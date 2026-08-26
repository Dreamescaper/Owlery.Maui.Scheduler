namespace Owlery.Maui.Scheduler.Tests;

internal sealed record TestAppointment(DateTime Start, DateTime End, string? Subject = null) : ISchedulerAppointment
{
    public static TestAppointment At(DateTime day, string time, double hours, string? subject = null)
    {
        var start = day.Date.Add(TimeSpan.Parse(time));
        return new TestAppointment(start, start.AddHours(hours), subject);
    }
}
