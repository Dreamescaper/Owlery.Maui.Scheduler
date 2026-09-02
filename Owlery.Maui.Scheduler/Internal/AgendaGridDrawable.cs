namespace Owlery.Maui.Scheduler.Internal;

/// <summary>
/// Paints the agenda's own furniture: the wash behind today's rows.
/// </summary>
/// <remarks>
/// Deliberately almost nothing. Rows carry their own borders from the host's template, and days read
/// as groups by the gap between them rather than by a drawn line — that is <see cref="AgendaGeometry.DayGap"/>'s
/// job. What is left to paint is the background that marks today's rows.
/// <para>
/// Only the rows in the dirty rectangle are walked. An agenda's canvas is as tall as its content —
/// far taller than a timeline's — so anything proportional to the whole range rather than to what is
/// on screen would be paid on every frame of a scroll.
/// </para>
/// </remarks>
internal sealed class AgendaGridDrawable(AgendaGeometry geometry, AgendaSurface surface) : IDrawable
{
    public Color CurrentDayBackgroundColor { get; set; } = null!;

    public bool ShowCurrentDayHighlight { get; set; } = true;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (!ShowCurrentDayHighlight)
            return;

        var rows = surface.Rows;

        if (rows.Count == 0 || geometry.ViewportWidth <= 0)
            return;

        var width = (float)geometry.ViewportWidth;
        var today = DateOnly.FromDateTime(geometry.Now);
        var bottom = dirtyRect.Bottom;

        canvas.FillColor = CurrentDayBackgroundColor;

        for (var i = AgendaLayoutEngine.IndexAt(rows, dirtyRect.Top); i < rows.Count && rows[i].Top <= bottom; i++)
        {
            var row = rows[i];

            if (row.Kind is AgendaRowKind.Appointment && row.Date == today)
                canvas.FillRectangle(0, (float)row.Top, width, (float)row.Height);
        }
    }
}
