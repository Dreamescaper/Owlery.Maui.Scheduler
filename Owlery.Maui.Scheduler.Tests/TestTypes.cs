using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Hosting;
using MC = Microsoft.Maui.Controls;
using MauiDispatching = Microsoft.Maui.Dispatching;

namespace Owlery.Maui.Scheduler.Tests;

/// <summary>
/// Spins up enough of MAUI for controls to behave as they would in an app, with no platform behind
/// them. Modelled on BlazorBindings.Maui's own unit-test host.
/// </summary>
internal static class TestApplication
{
    private sealed class ApplicationFromTestAssembly : MC.Application
    {
        protected override Window CreateWindow(IActivationState? activationState) => new();
    }

    public static (MC.Application Application, TestDispatcher Dispatcher) Create()
    {
        var dispatcher = new TestDispatcher();

        // Registered before Build as well: some MAUI internals resolve it during construction.
        DependencyService.RegisterSingleton<ISystemResourcesProvider>(new TestSystemResources());

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<ApplicationFromTestAssembly>();
        builder.Services.AddSingleton<MauiDispatching.IDispatcher>(dispatcher);

        var mauiApp = builder.Build();

        var application = mauiApp.Services.GetRequiredService<IApplication>();
        var mauiContext = new MauiContext(mauiApp.Services);

        application.Handler = new StubHandler { MauiContext = mauiContext, VirtualView = application };
        application.CreateWindow(new ActivationState(mauiContext));

        DependencyService.RegisterSingleton<ISystemResourcesProvider>(new TestSystemResources());

        return ((MC.Application)application, dispatcher);
    }

    /// <summary>A handler that exists only so elements have a MauiContext to find services through.</summary>
    private sealed class StubHandler : IElementHandler
    {
        public object? PlatformView => null;
        public IElement? VirtualView { get; set; }
        public IMauiContext? MauiContext { get; set; }
        public void DisconnectHandler() { }
        public void Invoke(string command, object? args = null) { }
        public void SetMauiContext(IMauiContext mauiContext) => MauiContext = mauiContext;
        public void SetVirtualView(IElement view) => VirtualView = view;
        public void UpdateValue(string property) { }
    }
}

#pragma warning disable CS0612 // ISystemResourcesProvider is obsolete but still resolved internally.
internal sealed class TestSystemResources : ISystemResourcesProvider
{
    public IResourceDictionary GetSystemResources() => new ResourceDictionary();
}
#pragma warning restore CS0612

/// <summary>
/// Runs everything inline and hands out timers the test drives by hand.
/// </summary>
/// <remarks>
/// The controllable timer is the point of this type. The scheduler expresses "held still long enough
/// to be a drag" and "the scroll has gone quiet" as dispatcher timers, so a test that cannot fire
/// them cannot reach either behaviour.
/// </remarks>
internal sealed class TestDispatcher : MauiDispatching.IDispatcher
{
    public List<TestDispatcherTimer> Timers { get; } = [];

    public bool IsDispatchRequired => false;

    public MauiDispatching.IDispatcherTimer CreateTimer()
    {
        var timer = new TestDispatcherTimer();
        Timers.Add(timer);
        return timer;
    }

    public bool Dispatch(Action action)
    {
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        action();
        return true;
    }

    /// <summary>Fires every timer currently running, as the platform would when its interval elapsed.</summary>
    public void FireRunningTimers()
    {
        foreach (var timer in Timers.Where(timer => timer.IsRunning).ToArray())
            timer.Fire();
    }

    /// <summary>Fires the running timer with the given interval — used to pick one of several.</summary>
    public void FireTimer(TimeSpan interval)
    {
        foreach (var timer in Timers.Where(timer => timer.IsRunning && timer.Interval == interval).ToArray())
            timer.Fire();
    }
}

internal sealed class TestDispatcherTimer : MauiDispatching.IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsRepeating { get; set; }
    public bool IsRunning { get; private set; }

    public event EventHandler? Tick;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void Fire()
    {
        if (!IsRepeating)
            IsRunning = false;

        Tick?.Invoke(this, EventArgs.Empty);
    }
}
