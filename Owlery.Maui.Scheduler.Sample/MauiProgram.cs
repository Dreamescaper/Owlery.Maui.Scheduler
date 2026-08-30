using Microsoft.Extensions.Logging;

#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace Owlery.Maui.Scheduler.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // The one line the control needs. Without it the horizontal pager falls back to the
            // handler for its base type and does not scroll.
            .UseOwleryScheduler()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();

        // Lets `maui devflow ui` tap, type and screenshot this app from a terminal. The control's
        // own project stays free of it — it has exactly one package reference, and this is the host's
        // tooling, not the control's.
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
    }
}
