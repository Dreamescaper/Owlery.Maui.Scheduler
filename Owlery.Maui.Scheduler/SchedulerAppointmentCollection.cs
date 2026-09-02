using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Owlery.Maui.Scheduler;

/// <summary>
/// An observable appointments collection that can be changed in ranges, each range operation
/// raising a single change notification.
/// </summary>
/// <typeparam name="T">
/// The appointment type — a class implementing <see cref="ISchedulerAppointment"/>. A reference type
/// because <see cref="IEnumerable{T}"/> is only covariant for one, and covariance is what lets a
/// collection of the host's own type be assigned to <see cref="SchedulerView.ItemsSource"/>.
/// </typeparam>
/// <remarks>
/// <see cref="ObservableCollection{T}"/> mutates one item at a time and raises one
/// <see cref="INotifyCollectionChanged.CollectionChanged"/> event per item, so adding a month of
/// appointments costs one event per appointment. This collection's range methods change the whole
/// range and raise one event for it — the framework's <c>ObservableCollection</c> deliberately has
/// no range methods
/// (see <seealso href="https://github.com/dotnet/runtime/issues/18087"/>), which is the pattern the
/// scheduler prefers: a host that loads in bulk appends with <see cref="AddRange"/> rather than
/// rebuilding and re-assigning <see cref="SchedulerView.ItemsSource"/>. The scheduler observes any
/// <see cref="INotifyCollectionChanged"/> and repaints, so either style works — this collection just
/// makes the bulk style one event instead of many.
/// <para>
/// The one event carries what changed. Every range method reports the precise action — an
/// <see cref="NotifyCollectionChangedAction.Add"/>, <see cref="NotifyCollectionChangedAction.Remove"/>
/// or <see cref="NotifyCollectionChangedAction.Replace"/> with its items and its starting index — for
/// every change that a single notification can describe. Some cannot be described by one:
/// <see cref="NotifyCollectionChangedEventArgs"/> states a multi-item add or remove as a run of items
/// from one index, so a scattered removal, or a replacement of a different length than the range it
/// replaces, has no faithful single-event form. Those report
/// <see cref="NotifyCollectionChangedAction.Reset"/> — "re-read the collection" — rather than a
/// payload that would misplace items. Each method's own documentation says which it raises, and a
/// consumer that reads payloads must handle <c>Reset</c> in any case, exactly as it must from
/// <see cref="Collection{T}.Clear"/>.
/// </para>
/// </remarks>
public class SchedulerAppointmentCollection<T> : ObservableCollection<T> where T : class, ISchedulerAppointment
{
    public SchedulerAppointmentCollection()
    {
    }

    public SchedulerAppointmentCollection(IEnumerable<T> collection)
        : base(collection)
    {
    }

    /// <summary>
    /// Appends every item in <paramref name="collection"/> and raises one
    /// <see cref="NotifyCollectionChangedAction.Add"/> carrying them, starting at the index the first
    /// one landed on.
    /// </summary>
    /// <remarks>
    /// An append is always a run of items at the end, so this is one of the operations a single
    /// notification describes exactly; it never reports <see cref="NotifyCollectionChangedAction.Reset"/>.
    /// A no-op when <paramref name="collection"/> is empty — no event is raised.
    /// </remarks>
    public void AddRange(IEnumerable<T> collection)
    {
        if (collection is null)
            throw new ArgumentNullException(nameof(collection));

        // Copied before anything is touched: the payload must be a snapshot the caller cannot go on
        // changing, and reading the source to the end first is what lets a collection be appended to
        // itself without the enumerator tripping over the writes.
        List<T> added = [.. collection];

        if (added.Count == 0)
            return;

        CheckReentrancy();

        var index = Items.Count;

        foreach (var item in added)
            Items.Add(item);

        RaiseChange(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, index),
            countChanged: true);
    }

    /// <summary>
    /// Removes the first occurrence of each item in <paramref name="collection"/> and raises one
    /// event: a <see cref="NotifyCollectionChangedAction.Remove"/> carrying the removed items when
    /// they sat together, and a <see cref="NotifyCollectionChangedAction.Reset"/> when they did not.
    /// </summary>
    /// <remarks>
    /// A multi-item <see cref="NotifyCollectionChangedAction.Remove"/> states one starting index for
    /// the whole run, so it can only describe items that were adjacent and are given in the order
    /// they sit in. Anything else — items from either end of the collection, or the same items listed
    /// out of order — is reported as <see cref="NotifyCollectionChangedAction.Reset"/>, because the
    /// alternative is an index that does not say where the items actually were. Use
    /// <see cref="RemoveRange(int, int)"/> when the window is known: it always carries its payload.
    /// <para>
    /// Items are matched with <see cref="EqualityComparer{T}.Default"/>, as they are in any
    /// collection — <em>not</em> by <see cref="ISchedulerAppointment.Key"/>, which is how the
    /// scheduler itself identifies an appointment. A host that has just built a changed appointment
    /// as a new instance must therefore pass the <em>old</em> instance here, or nothing is removed.
    /// </para>
    /// </remarks>
    public void RemoveRange(IEnumerable<T> collection)
    {
        if (collection is null)
            throw new ArgumentNullException(nameof(collection));

        List<T> requested = [.. collection];

        if (requested.Count == 0)
            return;

        CheckReentrancy();

        var removed = new List<T>(requested.Count);
        var removedAt = -1;
        var wereTogether = true;

        foreach (var item in requested)
        {
            var index = Items.IndexOf(item);

            if (index < 0)
                continue;

            // Adjacent items collapse onto the same index as the ones before them are taken out, so
            // one index for every removal is exactly the case a single Remove can describe.
            if (removed.Count == 0)
                removedAt = index;
            else if (index != removedAt)
                wereTogether = false;

            Items.RemoveAt(index);
            removed.Add(item);
        }

        if (removed.Count == 0)
            return;

        RaiseChange(
            wereTogether
                ? new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, removedAt)
                : Reset,
            countChanged: true);
    }

    /// <summary>
    /// Removes <paramref name="count"/> items starting at <paramref name="index"/> and raises one
    /// <see cref="NotifyCollectionChangedAction.Remove"/> carrying them and that index.
    /// </summary>
    /// <remarks>
    /// The window is contiguous by construction, so this never reports
    /// <see cref="NotifyCollectionChangedAction.Reset"/>. A <paramref name="count"/> of zero changes
    /// nothing and raises nothing.
    /// </remarks>
    public void RemoveRange(int index, int count)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (index + count > Count)
            throw new ArgumentException(
                "index and count do not denote a valid range of elements in the collection.");

        if (count == 0)
            return;

        CheckReentrancy();

        var removed = Window(index, count);

        for (var i = 0; i < count; i++)
            Items.RemoveAt(index);

        RaiseChange(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index),
            countChanged: true);
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> where <paramref name="original"/> sits, raising one
    /// <see cref="NotifyCollectionChangedAction.Replace"/>. Returns <c>false</c>, changing nothing,
    /// when <paramref name="original"/> is not in the collection.
    /// </summary>
    /// <remarks>
    /// The move-an-appointment case, in one call. A changed appointment is a new instance rather than
    /// one mutated in place (see <see cref="ISchedulerAppointment"/>), so a host settling a drop has
    /// to find the old instance and swap it — an index lookup, a bounds check and a range call, which
    /// every host would otherwise write for itself. Position is kept, so the collection does not
    /// reshuffle underneath a view that is already showing the appointment.
    /// <para>
    /// <paramref name="original"/> is matched with <see cref="EqualityComparer{T}.Default"/>, not by
    /// <see cref="ISchedulerAppointment.Key"/> — pass the instance that is in the collection, not the
    /// new one built to replace it.
    /// </para>
    /// </remarks>
    public bool Replace(T original, T replacement)
    {
        if (original is null)
            throw new ArgumentNullException(nameof(original));
        if (replacement is null)
            throw new ArgumentNullException(nameof(replacement));

        var index = Items.IndexOf(original);

        if (index < 0)
            return false;

        CheckReentrancy();

        var replaced = Items[index];
        Items[index] = replacement;

        RaiseChange(
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, replacement, replaced, index),
            countChanged: false);

        return true;
    }

    /// <summary>
    /// Replaces the <paramref name="count"/> items starting at <paramref name="index"/> with
    /// <paramref name="collection"/> and raises one event describing the change.
    /// </summary>
    /// <remarks>
    /// The replacement may be longer or shorter than the range it replaces: <paramref name="count"/>
    /// items are taken out and everything in <paramref name="collection"/> goes in their place, so
    /// the collection grows or shrinks by the difference. Items after the range keep their order and
    /// shift to make room or close the gap.
    /// <para>
    /// What it raises follows what actually happened. A same-length swap is a
    /// <see cref="NotifyCollectionChangedAction.Replace"/> carrying both the new and the old items;
    /// replacing nothing with items is an <see cref="NotifyCollectionChangedAction.Add"/>, and
    /// replacing items with nothing a <see cref="NotifyCollectionChangedAction.Remove"/>. A genuine
    /// change of length — some items out, a different number in — is the case no single notification
    /// describes, and reports <see cref="NotifyCollectionChangedAction.Reset"/>.
    /// </para>
    /// <para>
    /// Replacing the whole collection — <c>ReplaceRange(0, Count, everything)</c> — is the one-event
    /// way for a host to reload in bulk.
    /// </para>
    /// </remarks>
    public void ReplaceRange(int index, int count, IEnumerable<T> collection)
    {
        if (collection is null)
            throw new ArgumentNullException(nameof(collection));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (index + count > Count)
            throw new ArgumentException(
                "index and count do not denote a valid range of elements in the collection.");

        List<T> newItems = [.. collection];

        if (count == 0 && newItems.Count == 0)
            return;

        CheckReentrancy();

        var oldItems = Window(index, count);

        for (var i = 0; i < count && i < newItems.Count; i++)
            Items[index + i] = newItems[i];

        if (newItems.Count > count)
        {
            for (var i = count; i < newItems.Count; i++)
                Items.Insert(index + i, newItems[i]);
        }
        else
        {
            // Always at the same index: every removal shifts the tail down onto it. Walking the
            // index along with the loop would take out every other item instead.
            for (var i = newItems.Count; i < count; i++)
                Items.RemoveAt(index + newItems.Count);
        }

        RaiseChange(
            Describe(index, oldItems, newItems),
            countChanged: newItems.Count != count);
    }

    /// <summary>The single event that says what a replacement did, or <c>Reset</c> where none can.</summary>
    private static NotifyCollectionChangedEventArgs Describe(int index, List<T> oldItems, List<T> newItems) =>
        (oldItems.Count, newItems.Count) switch
        {
            (0, _) => new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItems, index),
            (_, 0) => new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItems, index),
            var (old, fresh) when old == fresh =>
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItems, oldItems, index),
            _ => Reset,
        };

    /// <summary>A snapshot of <paramref name="count"/> items from <paramref name="index"/>.</summary>
    private List<T> Window(int index, int count)
    {
        var window = new List<T>(count);

        for (var i = 0; i < count; i++)
            window.Add(Items[index + i]);

        return window;
    }

    private static NotifyCollectionChangedEventArgs Reset =>
        new(NotifyCollectionChangedAction.Reset);

    /// <remarks>
    /// <c>Count</c> is announced only when it moved, the way <see cref="ObservableCollection{T}"/>
    /// announces it — a same-length replacement changes the items, not how many there are.
    /// </remarks>
    private void RaiseChange(NotifyCollectionChangedEventArgs change, bool countChanged)
    {
        if (countChanged)
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));

        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(change);
    }
}
