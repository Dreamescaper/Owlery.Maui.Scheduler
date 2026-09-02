using Microsoft.Maui.Hosting;

namespace Owlery.Maui.Scheduler;

public static class AppHostBuilderExtensions
{
    /// <summary>
    /// Registers the control's platform handlers. Call it from <c>MauiProgram</c>.
    /// </summary>
    /// <remarks>
    /// MAUI has no way for a library to register a handler on its own, so this is the one line a host
    /// has to add. Without it <see cref="PagingScrollView"/> falls back to the handler for its base
    /// type and simply does not scroll — see docs/design/pager.md.
    /// <para>
    /// A no-op where there is no platform, which is what lets the headless tests run without it.
    /// </para>
    /// </remarks>
    public static MauiAppBuilder UseOwleryScheduler(this MauiAppBuilder builder)
    {
#if IOS || MACCATALYST || ANDROID
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<PagingScrollView, Handlers.PagingScrollViewHandler>());
#endif
        return builder;
    }
}
