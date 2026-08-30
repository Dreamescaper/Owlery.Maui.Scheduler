using System.ComponentModel;
using System.Globalization;

namespace Owlery.Maui.Scheduler.Internal;

/// <summary>Lets XAML set a <see cref="TimeOnly"/> property from text such as <c>09:00</c>.</summary>
/// <remarks>
/// MAUI's XAML loader does not consult the converter the framework registers for
/// <see cref="TimeOnly"/>, so without this a <see cref="TimeOnly"/> property cannot be set from XAML
/// at all — it fails at load with "mismatching type between value and property". Markup is parsed
/// with the invariant culture, so a page reads the same on every device.
/// </remarks>
internal sealed class TimeOnlyTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text
            ? TimeOnly.Parse(text, CultureInfo.InvariantCulture)
            : throw new NotSupportedException($"Cannot convert \"{value}\" into a {nameof(TimeOnly)}.");
}
