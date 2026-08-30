using System.Reflection;
using Microsoft.Maui.Graphics;

namespace Owlery.Maui.Scheduler.Tests;

internal class RecordingCanvas : DispatchProxy
{
    private Color? fillColor;
    private Color? strokeColor;

    public List<FillOperation> Fills { get; } = [];

    public List<LineOperation> Lines { get; } = [];

    public static (ICanvas Canvas, RecordingCanvas Recording) Create()
    {
        var canvas = DispatchProxy.Create<ICanvas, RecordingCanvas>();
        return (canvas, (RecordingCanvas)(object)canvas);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        switch (targetMethod.Name)
        {
            case "set_FillColor":
                fillColor = (Color?)args![0];
                break;
            case "set_StrokeColor":
                strokeColor = (Color?)args![0];
                break;
            case nameof(ICanvas.FillRectangle):
                Fills.Add(new FillOperation(fillColor!, Rectangle(args!)));
                break;
            case nameof(ICanvas.DrawLine):
                Lines.Add(new LineOperation(
                    strokeColor!,
                    new PointF(Convert.ToSingle(args![0]), Convert.ToSingle(args[1])),
                    new PointF(Convert.ToSingle(args[2]), Convert.ToSingle(args[3]))));
                break;
        }

        if (targetMethod.ReturnType == typeof(void))
            return null;

        return targetMethod.ReturnType.IsValueType
            ? Activator.CreateInstance(targetMethod.ReturnType)
            : null;
    }

    private static RectF Rectangle(object?[] args) => args.Length switch
    {
        1 => (RectF)args[0]!,
        _ => new RectF(
            Convert.ToSingle(args[0]),
            Convert.ToSingle(args[1]),
            Convert.ToSingle(args[2]),
            Convert.ToSingle(args[3]))
    };
}

internal readonly record struct FillOperation(Color Color, RectF Bounds);

internal readonly record struct LineOperation(Color Color, PointF Start, PointF End);
