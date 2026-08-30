#if IOS || MACCATALYST || ANDROID
using Microsoft.Maui.Handlers;

namespace Owlery.Maui.Scheduler.Handlers;

internal partial class PagingScrollViewHandler
{
    public static IPropertyMapper<PagingScrollView, PagingScrollViewHandler> Mapper =
        new PropertyMapper<PagingScrollView, PagingScrollViewHandler>(ViewMapper)
        {
            [nameof(PagingScrollView.Content)] = MapContent,
            [nameof(PagingScrollView.IsScrollEnabled)] = MapIsScrollEnabled,
            [nameof(PagingScrollView.PageWidth)] = MapPageWidth
        };

    public PagingScrollViewHandler()
        : base(Mapper)
    {
    }

    public PagingScrollViewHandler(IPropertyMapper? mapper)
        : base(mapper ?? Mapper)
    {
    }
}
#endif
