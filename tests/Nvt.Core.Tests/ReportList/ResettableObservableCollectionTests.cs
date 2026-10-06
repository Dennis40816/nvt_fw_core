// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Collections.Specialized;
using System.ComponentModel;
using Nvt.Core.ReportList;
using Xunit;

namespace Nvt.Core.Tests.ReportList;

/// <summary>Characterizes bulk notifications and synthetic report and memory publication.</summary>
public sealed class ResettableObservableCollectionTests
{
    private static readonly string[] ReplacementNotifications = ["Count", "Item[]", "Reset"];
    private static readonly string[] CountNotification = ["Count"];
    private static readonly int[] FirstItem = [1];
    private static readonly int[] SecondItem = [2];
    private static readonly int[] TwoItems = [1, 2];

    /// <summary>Replacement preserves identity and publishes complete contents in the frozen notification order.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(10_000)]
    public void ReplaceAllKeepsIdentityAndRaisesCountItemAndOneReset(int count)
    {
        var collection = new ResettableObservableCollection<object> { new() };
        object original = collection;
        object[] expected = [.. Enumerable.Range(0, count).Select(_ => new object())];
        var notifications = new List<string>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (sender, args) =>
        {
            Assert.Same(original, sender);
            Assert.Equal(expected, collection);
            notifications.Add(args.PropertyName!);
        };
        collection.CollectionChanged += (sender, args) =>
        {
            Assert.Same(original, sender);
            Assert.Equal(expected, collection);
            Assert.Equal(NotifyCollectionChangedAction.Reset, args.Action);
            Assert.Null(args.NewItems);
            Assert.Null(args.OldItems);
            Assert.Equal(-1, args.NewStartingIndex);
            Assert.Equal(-1, args.OldStartingIndex);
            notifications.Add(args.Action.ToString());
        };

        collection.ReplaceAll(expected);

        Assert.Same(original, collection);
        Assert.Equal(expected, collection);
        Assert.Equal(ReplacementNotifications, notifications);
        for (int index = 0; index < count; index++)
        {
            Assert.Same(expected[index], collection[index]);
        }
    }

    /// <summary>The frozen navigation assertion applies to every replacement of a synthetic 64-row window.</summary>
    [Fact]
    public void WindowReplacementRaisesOneResetForEveryPage()
    {
        int[] source = [.. Enumerable.Range(0, 130)];
        var collection = new ResettableObservableCollection<int>();
        collection.ReplaceAll(source.Take(64));
        object original = collection;
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => collectionChanges.Add(args.Action);

        collection.ReplaceAll(source.Skip(64).Take(64));

        Assert.Equal(64, collection.Count);
        Assert.Equal(64, collection[0]);
        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);

        collectionChanges.Clear();
        collection.ReplaceAll(source.Skip(128).Take(64));

        Assert.Equal(2, collection.Count);
        Assert.Equal(128, collection[0]);
        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);
        Assert.Same(original, collection);
    }

    /// <summary>Repeated memory-style publication stays within the frozen Reset bounds and preserves observed states.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SyntheticMemoryPublicationUsesOnlyResetsAndKeepsPublishedStates(int maxNotifications)
    {
        SyntheticSlice[] expected = [new(new object()), new(new object()), new(new object()), new(new object())];
        var collection = new ResettableObservableCollection<SyntheticSlice>();
        var actions = new List<NotifyCollectionChangedAction>();
        var published = new List<(SyntheticSlice Slice, object State)>();
        int observedRows = 0;
        collection.CollectionChanged += (_, args) =>
        {
            actions.Add(args.Action);
            published.Clear();
            published.AddRange(collection.Select(static slice => (slice, slice.Interaction)));
            observedRows += collection.Count;
        };

        for (int publication = 0; publication < maxNotifications; publication++)
        {
            collection.ReplaceAll(expected);
        }

        AssertOnlyResetsWithinBounds(actions, maxNotifications);
        Assert.True(collection.Count > 2,
            "The fixture must keep enough segments that per-item Add fan-out would be detected.");
        Assert.Equal(expected.Length * maxNotifications, observedRows);
        Assert.NotEmpty(published);
        Assert.Equal(expected, published.Select(static entry => entry.Slice));
        Assert.All(published, entry => Assert.Same(entry.State, entry.Slice.Interaction));
    }

    /// <summary>Empty and identical replacements still publish all three notifications.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnchangedContentsStillRaiseReplacementNotifications(int count)
    {
        int[] expected = [.. Enumerable.Range(0, count)];
        var collection = new ResettableObservableCollection<int>();
        collection.ReplaceAll(expected);
        var notifications = new List<string>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) => notifications.Add(args.PropertyName!);
        collection.CollectionChanged += (_, args) => notifications.Add(args.Action.ToString());

        collection.ReplaceAll(expected);

        Assert.Equal(expected, collection);
        Assert.Equal(ReplacementNotifications, notifications);
    }

    /// <summary>Null input fails before clearing or notifying.</summary>
    [Fact]
    public void NullInputPreservesContentsWithoutNotifications()
    {
        var collection = new ResettableObservableCollection<int> { 7 };
        var notifications = new List<string>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) => notifications.Add(args.PropertyName!);
        collection.CollectionChanged += (_, args) => notifications.Add(args.Action.ToString());

        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null!));

        Assert.Equal("items", error.ParamName);
        Assert.Equal(7, Assert.Single(collection));
        Assert.Empty(notifications);
    }

    /// <summary>The collection clears before input enumeration and retains any yielded prefix on failure.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnumerationFailureLeavesPrefixWithoutNotifications(bool yieldItem)
    {
        var collection = new ResettableObservableCollection<int> { 7, 8 };
        var failure = new InvalidOperationException("Synthetic enumeration failure.");
        var notifications = new List<string>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) => notifications.Add(args.PropertyName!);
        collection.CollectionChanged += (_, args) => notifications.Add(args.Action.ToString());

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => collection.ReplaceAll(Items())));

        Assert.Equal(yieldItem ? FirstItem : Array.Empty<int>(), collection);
        Assert.Empty(notifications);

        IEnumerable<int> Items()
        {
            Assert.Empty(collection);
            if (yieldItem)
            {
                yield return 1;
            }

            throw failure;
        }
    }

    /// <summary>Replacing from the same collection enumerates the already-cleared contents.</summary>
    [Fact]
    public void SelfReplacementClearsTheCollectionAndRaisesOneReset()
    {
        var collection = new ResettableObservableCollection<int> { 7, 8 };
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => actions.Add(args.Action);

        collection.ReplaceAll(collection);

        Assert.Empty(collection);
        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
    }

    /// <summary>Two listeners cause reentrant replacement to fail while null validation still runs first.</summary>
    [Fact]
    public void ReentrantReplacementWithTwoListenersIsRejectedBeforeClearing()
    {
        var collection = new ResettableObservableCollection<int>();
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, _) =>
        {
            ArgumentNullException nullError = Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null!));
            Assert.Equal("items", nullError.ParamName);
            Assert.Throws<InvalidOperationException>(() => collection.ReplaceAll([2]));
        };
        collection.CollectionChanged += (_, args) => actions.Add(args.Action);

        collection.ReplaceAll([1]);

        Assert.Equal(1, Assert.Single(collection));
        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
    }

    /// <summary>A single listener permits reentrant replacement under ObservableCollection's existing rule.</summary>
    [Fact]
    public void ReentrantReplacementWithOneListenerIsAllowed()
    {
        var collection = new ResettableObservableCollection<int>();
        var snapshots = new List<int[]>();
        bool nested = false;
        collection.CollectionChanged += (_, _) =>
        {
            snapshots.Add([.. collection]);
            if (!nested)
            {
                nested = true;
                collection.ReplaceAll([2]);
            }
        };

        collection.ReplaceAll([1]);

        Assert.Equal(2, Assert.Single(collection));
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(FirstItem, snapshots[0]);
        Assert.Equal(SecondItem, snapshots[1]);
    }

    /// <summary>A property listener failure stops later notifications after replacement is complete.</summary>
    [Fact]
    public void PropertyListenerFailureStopsLaterNotifications()
    {
        var collection = new ResettableObservableCollection<int> { 7 };
        var failure = new InvalidOperationException("Synthetic observer failure.");
        var notifications = new List<string>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) =>
        {
            notifications.Add(args.PropertyName!);
            throw failure;
        };
        collection.CollectionChanged += (_, args) => notifications.Add(args.Action.ToString());

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => collection.ReplaceAll([1, 2])));

        Assert.Equal(TwoItems, collection);
        Assert.Equal(CountNotification, notifications);
    }

    private static void AssertOnlyResetsWithinBounds(List<NotifyCollectionChangedAction> actions, int maxNotifications)
    {
        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.Equal(NotifyCollectionChangedAction.Reset, action));
        Assert.True(actions.Count <= maxNotifications,
            $"Expected at most {maxNotifications} Reset notification(s), observed {actions.Count}.");
    }

    private sealed record SyntheticSlice(object Interaction);
}
