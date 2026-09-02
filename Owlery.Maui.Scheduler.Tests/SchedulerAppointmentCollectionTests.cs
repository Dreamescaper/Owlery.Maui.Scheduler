using System.Collections.Specialized;
using System.ComponentModel;

namespace Owlery.Maui.Scheduler.Tests;

[TestFixture]
public class SchedulerAppointmentCollectionTests
{
    private SchedulerAppointmentCollection<TestAppointment> collection = null!;
    private List<NotifyCollectionChangedEventArgs> events = null!;
    private List<string> propertyNames = null!;

    /// <summary>The single event a range operation raised, failing the test if it raised any other number.</summary>
    private NotifyCollectionChangedEventArgs TheEvent
    {
        get
        {
            Assert.That(events, Has.Count.EqualTo(1), "a range operation raises exactly one event");
            return events[0];
        }
    }

    private static IEnumerable<string?> SubjectsOf(System.Collections.IList? items) =>
        items?.Cast<TestAppointment>().Select(a => a.Subject) ?? [];

    [SetUp]
    public void SetUp()
    {
        collection = new SchedulerAppointmentCollection<TestAppointment>();
        events = [];
        propertyNames = [];

        collection.CollectionChanged += (_, e) => events.Add(e);
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName ?? string.Empty);
    }

    private static TestAppointment At(string subject, TimeSpan from, TimeSpan to)
        => new(DateTime.Today.Add(from), DateTime.Today.Add(to), subject);

    /// <summary>
    /// Fills the shared, subscribed collection and clears the recorded notifications, so the test
    /// measures only the range operation under test.
    /// </summary>
    private void Seed(int count)
    {
        for (var i = 0; i < count; i++)
            collection.Add(At($"a{i}", TimeSpan.FromHours(9), TimeSpan.FromHours(10)));
        events.Clear();
        propertyNames.Clear();
    }

    [Test]
    public void AddRange_appends_every_item_and_raises_one_Add_carrying_them()
    {
        Seed(2);
        var additions = new[] { At("x", TimeSpan.FromHours(9), TimeSpan.FromHours(10)), At("y", TimeSpan.FromHours(11), TimeSpan.FromHours(12)) };

        collection.AddRange(additions);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "a1", "x", "y" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
            Assert.That(TheEvent.NewItems, Is.EqualTo(additions));
            Assert.That(TheEvent.NewStartingIndex, Is.EqualTo(2), "where the first one landed");
            Assert.That(TheEvent.OldItems, Is.Null);
            Assert.That(propertyNames, Is.EqualTo(new[] { nameof(collection.Count), "Item[]" }));
        });
    }

    [Test]
    public void AddRange_can_append_the_collection_to_itself()
    {
        // The source is read to the end before anything is written, so appending a collection to
        // itself neither trips the enumerator nor runs away.
        Seed(2);

        collection.AddRange(collection);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "a1", "a0", "a1" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
            Assert.That(TheEvent.NewStartingIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void AddRange_of_nothing_raises_no_events()
    {
        collection.AddRange(Array.Empty<TestAppointment>());

        Assert.Multiple(() =>
        {
            Assert.That(collection, Is.Empty);
            Assert.That(events, Is.Empty);
            Assert.That(propertyNames, Is.Empty);
        });
    }

    [Test]
    public void AddRange_of_null_throws()
    {
        Assert.That(() => collection.AddRange(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void RemoveRange_of_items_that_sat_together_raises_one_Remove_carrying_them()
    {
        Seed(4);
        var toRemove = new[] { collection[1], collection[2] };

        collection.RemoveRange(toRemove);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "a3" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Remove));
            Assert.That(TheEvent.OldItems, Is.EqualTo(toRemove));
            Assert.That(TheEvent.OldStartingIndex, Is.EqualTo(1), "where the run began");
            Assert.That(TheEvent.NewItems, Is.Null);
        });
    }

    [Test]
    public void RemoveRange_of_scattered_items_raises_one_Reset()
    {
        // One Remove states a single index for the whole run, so items from either end of the
        // collection have no honest payload — a Reset says "re-read me" instead of naming a place
        // neither of them was.
        Seed(3);
        var toRemove = new[] { collection[0], collection[2] };

        collection.RemoveRange(toRemove);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a1" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Reset));
            Assert.That(TheEvent.OldItems, Is.Null, "a Reset names nothing");
            Assert.That(TheEvent.NewItems, Is.Null);
        });
    }

    [Test]
    public void RemoveRange_of_items_that_are_not_there_raises_no_events()
    {
        Seed(2);

        collection.RemoveRange([At("absent", TimeSpan.FromHours(9), TimeSpan.FromHours(10))]);

        Assert.Multiple(() =>
        {
            Assert.That(collection, Has.Count.EqualTo(2));
            Assert.That(events, Is.Empty);
            Assert.That(propertyNames, Is.Empty);
        });
    }

    [Test]
    public void RemoveRange_of_no_items_raises_no_events()
    {
        Seed(2);

        collection.RemoveRange(0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(collection, Has.Count.EqualTo(2));
            Assert.That(events, Is.Empty);
            Assert.That(propertyNames, Is.Empty);
        });
    }

    [Test]
    public void RemoveRange_by_index_removes_the_window_and_raises_one_Remove_carrying_it()
    {
        Seed(5);

        collection.RemoveRange(1, 2);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "a3", "a4" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Remove));
            Assert.That(SubjectsOf(TheEvent.OldItems), Is.EqualTo(new[] { "a1", "a2" }));
            Assert.That(TheEvent.OldStartingIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void RemoveRange_with_invalid_bounds_throws()
    {
        Seed(3);

        Assert.Multiple(() =>
        {
            Assert.That(() => collection.RemoveRange(-1, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => collection.RemoveRange(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => collection.RemoveRange(2, 2), Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void ReplaceRange_of_equal_length_raises_one_Replace_carrying_both_sides()
    {
        Seed(4);
        var replacement = new[] { At("new0", TimeSpan.FromHours(8), TimeSpan.FromHours(9)), At("new1", TimeSpan.FromHours(8), TimeSpan.FromHours(9)) };

        collection.ReplaceRange(1, 2, replacement);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "new0", "new1", "a3" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Replace));
            Assert.That(TheEvent.NewItems, Is.EqualTo(replacement));
            Assert.That(SubjectsOf(TheEvent.OldItems), Is.EqualTo(new[] { "a1", "a2" }));
            Assert.That(TheEvent.NewStartingIndex, Is.EqualTo(1));
            Assert.That(
                propertyNames,
                Is.EqualTo(new[] { "Item[]" }),
                "the items changed, not how many there are");
        });
    }

    [Test]
    public void ReplaceRange_of_nothing_with_items_raises_one_Add()
    {
        // Nothing came out, so it is an insertion and says so.
        Seed(3);
        var inserted = new[] { At("new0", TimeSpan.FromHours(8), TimeSpan.FromHours(9)) };

        collection.ReplaceRange(1, 0, inserted);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "new0", "a1", "a2" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Add));
            Assert.That(TheEvent.NewItems, Is.EqualTo(inserted));
            Assert.That(TheEvent.NewStartingIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReplaceRange_of_items_with_nothing_raises_one_Remove()
    {
        Seed(3);

        collection.ReplaceRange(1, 2, []);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Remove));
            Assert.That(SubjectsOf(TheEvent.OldItems), Is.EqualTo(new[] { "a1", "a2" }));
            Assert.That(TheEvent.OldStartingIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void ReplaceRange_longer_than_the_window_inserts_the_extra_items()
    {
        Seed(4);
        var replacement = new[]
        {
            At("new0", TimeSpan.FromHours(8), TimeSpan.FromHours(9)),
            At("new1", TimeSpan.FromHours(8), TimeSpan.FromHours(9)),
            At("new2", TimeSpan.FromHours(8), TimeSpan.FromHours(9)),
        };

        collection.ReplaceRange(1, 2, replacement);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "new0", "new1", "new2", "a3" }));
            Assert.That(collection.Count, Is.EqualTo(5));
            Assert.That(
                TheEvent.Action,
                Is.EqualTo(NotifyCollectionChangedAction.Reset),
                "two out and three in is no single Add, Remove or Replace");
        });
    }

    [Test]
    public void ReplaceRange_shorter_than_the_window_removes_the_extra_items()
    {
        Seed(4);
        var replacement = new[] { At("new0", TimeSpan.FromHours(8), TimeSpan.FromHours(9)) };

        collection.ReplaceRange(1, 2, replacement);

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(a => a.Subject), Is.EqualTo(new[] { "a0", "new0", "a3" }));
            Assert.That(collection.Count, Is.EqualTo(3));
            Assert.That(
                TheEvent.Action,
                Is.EqualTo(NotifyCollectionChangedAction.Reset),
                "two out and one in is no single Add, Remove or Replace");
        });
    }

    [Test]
    public void ReplaceRange_shorter_by_more_than_one_removes_a_contiguous_window()
    {
        // The removals all land on the same index — each one shifts the tail down onto it. Walking
        // the index along with the loop took out every other item, and ran off the end when the
        // tail was short.
        Seed(10);

        collection.ReplaceRange(1, 4, [At("new0", TimeSpan.FromHours(8), TimeSpan.FromHours(9))]);

        Assert.Multiple(() =>
        {
            Assert.That(
                collection.Select(a => a.Subject),
                Is.EqualTo(new[] { "a0", "new0", "a5", "a6", "a7", "a8", "a9" }));
            Assert.That(TheEvent.Action, Is.EqualTo(NotifyCollectionChangedAction.Reset));
        });
    }

    [Test]
    public void ReplaceRange_of_nothing_with_nothing_raises_no_events()
    {
        Seed(2);

        collection.ReplaceRange(1, 0, []);

        Assert.Multiple(() =>
        {
            Assert.That(collection, Has.Count.EqualTo(2));
            Assert.That(events, Is.Empty);
            Assert.That(propertyNames, Is.Empty);
        });
    }

    [Test]
    public void ReplaceRange_of_null_or_bad_bounds_throws()
    {
        Seed(3);

        Assert.Multiple(() =>
        {
            Assert.That(() => collection.ReplaceRange(0, 1, null!), Throws.ArgumentNullException);
            Assert.That(() => collection.ReplaceRange(-1, 1, [At("x", TimeSpan.FromHours(1), TimeSpan.FromHours(2))]), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => collection.ReplaceRange(2, 2, [At("x", TimeSpan.FromHours(1), TimeSpan.FromHours(2))]), Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void A_range_operation_is_one_event_regardless_of_item_count()
    {
        // Mutation-check for the "one event" promise: if the implementation ever falls back to a
        // per-item loop this counts many events and fails.
        var huge = Enumerable.Range(0, 500).Select(i => At($"h{i}", TimeSpan.FromHours(1), TimeSpan.FromHours(2))).ToArray();

        collection.AddRange(huge);
        Assert.That(events, Has.Count.EqualTo(1), "AddRange of 500");

        collection.RemoveRange(0, 250);
        Assert.That(events, Has.Count.EqualTo(2), "RemoveRange of 250 by index");

        collection.RemoveRange(huge.Skip(250).Take(10));
        Assert.That(events, Has.Count.EqualTo(3), "RemoveRange of 10 by value");

        collection.ReplaceRange(0, 5, huge.Skip(100).Take(20));
        Assert.That(events, Has.Count.EqualTo(4), "ReplaceRange of 5 with 20");
    }
}
