namespace Owlery.Maui.Scheduler.Sample.Data;

/// <summary>Flat colour set the appointment templates draw from, indexed by appointment.</summary>
public static class SamplePalette
{
    private static readonly (Color Fill, Color Ink)[] Entries =
    [
        (Color.FromArgb("#DCE9F7"), Color.FromArgb("#1B4B7A")),
        (Color.FromArgb("#E2F1E4"), Color.FromArgb("#245C33")),
        (Color.FromArgb("#FBE7DA"), Color.FromArgb("#8A4416")),
        (Color.FromArgb("#EDE3F7"), Color.FromArgb("#553281")),
        (Color.FromArgb("#FCECF1"), Color.FromArgb("#8C2B4A")),
        (Color.FromArgb("#E4F1F4"), Color.FromArgb("#1D5A66")),
        (Color.FromArgb("#F6EFD9"), Color.FromArgb("#75581A")),
        (Color.FromArgb("#E7E9EE"), Color.FromArgb("#3B4356"))
    ];

    public static int Count => Entries.Length;

    public static Color Fill(int index) => Entries[Wrap(index)].Fill;

    public static Color Ink(int index) => Entries[Wrap(index)].Ink;

    private static int Wrap(int index) => ((index % Entries.Length) + Entries.Length) % Entries.Length;
}
