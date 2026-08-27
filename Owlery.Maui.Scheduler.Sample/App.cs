using Owlery.Maui.Scheduler.Sample.Playground;

namespace Owlery.Maui.Scheduler.Sample;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new PlaygroundPage());
}
