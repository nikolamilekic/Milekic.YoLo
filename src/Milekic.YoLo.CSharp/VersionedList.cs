using System.Collections;
using System.Collections.Immutable;

namespace Milekic.YoLo;

public record VersionedList<T> : IReadOnlyList<T>
{
    public abstract record Change
    {
        public interface IVisitor
        {
            void Visit(AddRange addRange);
            void Visit(RemoveRange removeRange);
            void Visit(InsertRange insertRange);
            void Visit(SetItem setItem);
            void Visit(ReplaceItem replaceItem);
            void Visit(RemoveAt removeAt);
        }

        private Change() { }

        public abstract void Accept(IVisitor current);

        public record AddRange(ImmutableArray<T> Items) : Change
        {
            public override void Accept(IVisitor visitor) => visitor.Visit(this);
        }
        public record RemoveRange(ImmutableArray<T> Items) : Change
        {
            public override void Accept(IVisitor visitor) => visitor.Visit(this);
        }
        public record RemoveAt(int Index) : Change
        {
            public override void Accept(IVisitor visitor) => visitor.Visit(this);
        }
        public record InsertRange(int Index, ImmutableArray<T> Items) : Change
        {
            public override void Accept(IVisitor visitor) => visitor.Visit(this);
        }
        public record SetItem(int Index, T Item) : Change
        {
            public override void Accept(IVisitor visitor) => visitor.Visit(this);
        }
        public record ReplaceItem(T OldItem, T NewItem) : Change
        {
            public override void Accept(IVisitor visitor) => visitor.Visit(this);
        }
    }

    public class ChangeFolder : Change.IVisitor
    {
        private readonly ImmutableList<T>.Builder Builder;
        public ChangeFolder(ImmutableList<T> source)
        {
            Builder = source.ToBuilder();
        }
        public ImmutableList<T> GetResult() => Builder.ToImmutableList();

        public void Visit(Change.AddRange addRange) => Builder.AddRange(addRange.Items);
        public void Visit(Change.RemoveRange removeRange)
        {
            foreach (var item in removeRange.Items)
            {
                Builder.Remove(item);
            }
        }
        public void Visit(Change.InsertRange insertRange) =>
            Builder.InsertRange(insertRange.Index, insertRange.Items);
        public void Visit(Change.SetItem setItem) => Builder[setItem.Index] = setItem.Item;
        public void Visit(Change.ReplaceItem replaceItem) =>
            Builder[Builder.IndexOf(replaceItem.OldItem)] = replaceItem.NewItem;
        public void Visit(Change.RemoveAt removeAt) => Builder.RemoveAt(removeAt.Index);
    }

    public readonly ImmutableList<T> Store;
    public readonly ImmutableList<Change> History;
    private readonly ImmutableList<int> HashCodes;

    public int Version => History.Count;
    public int Count => Store.Count;
    public T this[int index] => Store[index];

    public VersionedList() : this(
        ImmutableList<T>.Empty,
        ImmutableList<Change>.Empty,
        ImmutableList<int>.Empty) { }
    private VersionedList(ImmutableList<T> store, ImmutableList<Change> history, ImmutableList<int> hashCodes)
    {
        Store = store;
        History = history;
        HashCodes = hashCodes;
    }

    public IEnumerable<Change> GetChangesSince(VersionedList<T> old)
    {
        var firstNewChange = old.Version + 1;
        if (firstNewChange >= Version || HashCodes[firstNewChange] != old.GetHashCode())
        {
            throw new ArgumentException("Current version is not a newer version of old");
        }
        return History.Skip(old.Version);
    }

    public IEnumerator<T> GetEnumerator() => Store.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Store).GetEnumerator();

    public VersionedList<T> Add(T item) => new(
        Store.Add(item),
        History.Add(new Change.AddRange(ImmutableArray.Create(item))),
        HashCodes.Add(GetHashCode()));
    public VersionedList<T> AddRange(IEnumerable<T> items)
    {
        var array = items.ToImmutableArray();
        return new(
            Store.AddRange(array),
            History.Add(new Change.AddRange(array)),
            HashCodes.Add(GetHashCode()));
    }
    public VersionedList<T> Insert(int index, T item) => new(
        Store.Insert(index, item),
        History.Add(new Change.InsertRange(index, ImmutableArray.Create(item))),
        HashCodes.Add(GetHashCode()));
    public VersionedList<T> InsertRange(int index, IEnumerable<T> items)
    {
        var array = items.ToImmutableArray();
        return new(
            Store.InsertRange(index, array),
            History.Add(new Change.InsertRange(index, array)),
            HashCodes.Add(GetHashCode()));
    }
    public VersionedList<T> Remove(T item) => new(
        Store.Remove(item),
        History.Add(new Change.RemoveRange(ImmutableArray.Create(item))),
        HashCodes.Add(GetHashCode()));
    public VersionedList<T> RemoveRange(IEnumerable<T> items)
    {
        var array = items.ToImmutableArray();
        return new(
            Store.RemoveRange(array),
            History.Add(new Change.RemoveRange(array)),
            HashCodes.Add(GetHashCode()));
    }
    public VersionedList<T> RemoveAt(int index) => new(
        Store.RemoveAt(index),
        History.Add(new Change.RemoveAt(index)),
        HashCodes.Add(GetHashCode()));
    public VersionedList<T> SetItem(int index, T value) => new(
        Store.SetItem(index, value),
        History.Add(new Change.SetItem(index, value)),
        HashCodes.Add(GetHashCode()));
    public VersionedList<T> Replace(T oldItem, T newItem) => new(
        Store.Replace(oldItem, newItem),
        History.Add(new Change.ReplaceItem(oldItem, newItem)),
        HashCodes.Add(GetHashCode()));
}
