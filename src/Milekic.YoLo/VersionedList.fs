namespace Milekic.YoLo

open System.Collections.Generic
open System.Collections.Immutable

module VersionedList =
    type Change<'T> =
        | Add of 'T
        | AddRange of ImmutableArray<'T>
        | Remove of 'T
        | RemoveAt of int
        | Insert of int * 'T
        | InsertRange of int * ImmutableArray<'T>
        | SetItem of int * 'T
        | Replace of 'T * 'T
    let makeChangeBuilder (list : ImmutableList<_>) =
        let builder = list.ToBuilder()
        let folder =
            function
            | Add item -> builder.Add item
            | AddRange items -> builder.AddRange items
            | Remove item -> builder.Remove item |> ignore
            | RemoveAt index -> builder.RemoveAt index
            | Insert (index, item) -> builder.Insert(index, item)
            | InsertRange (index, items) -> builder.InsertRange(index, items)
            | SetItem (index, item) -> builder[index] <- item
            | Replace (oldItem, newItem) ->
                let index = builder.IndexOf oldItem
                if index >= 0 then builder[index] <- newItem
        let runner = fun () -> builder.ToImmutable()
        folder, runner

open VersionedList

type VersionedList<'T when 'T : equality> private (store, history, hashCodes) =
    new() = VersionedList(ImmutableList.Empty, ImmutableList.Empty,  ImmutableList.Empty)

    member _.Store = store
    member _.History = history
    member _.Version = history.Count
    member private _.State = (store, history, hashCodes)

    member this.GetChangesSince(other : VersionedList<'T>) =
        let firstNewChange = other.Version + 1
        if firstNewChange >= this.Version then None else

        if (hashCodes[firstNewChange] <> other.GetHashCode())
        then None
        else let x = history |> Seq.toArray in Some x[firstNewChange..]

    override this.Equals(obj) =
        match obj with
        | null -> false
        | _ when LanguagePrimitives.PhysicalEquality (box this) obj -> true
        | _ when obj.GetType() <> this.GetType() -> false
        | _ -> this.Equals(obj :?> VersionedList<'T>)
    member this.Equals(other : VersionedList<'T>) = this.State = other.State
    override this.GetHashCode() = this.State.GetHashCode()

    member _.ClearHistory () =
        VersionedList(store, ImmutableList.Empty, ImmutableList.Empty)
    member this.Add(item) =
        VersionedList(
            store.Add(item),
            history.Add(Add item),
            hashCodes.Add(this.GetHashCode()))
    member this.AddRange(items : 'T seq) =
        let items = items.ToImmutableArray()
        VersionedList(
            store.AddRange(items),
            history.Add(AddRange items),
            hashCodes.Add(this.GetHashCode()))
    member this.Insert(index, item) =
        VersionedList(
            store.Insert(index, item),
            history.Add(Insert(index, item)),
            hashCodes.Add(this.GetHashCode()))
    member this.InsertRange(index, items : 'T seq) =
        let items = items.ToImmutableArray()
        VersionedList(
            store.InsertRange(index, items),
            history.Add(InsertRange(index, items)),
            hashCodes.Add(this.GetHashCode()))
    member this.Remove(item) =
        VersionedList(
            store.Remove item,
            history.Add(Remove item),
            hashCodes.Add(this.GetHashCode()))
    member this.RemoveAt(index) =
        VersionedList(
            store.RemoveAt index,
            history.Add(RemoveAt index),
            hashCodes.Add(this.GetHashCode()))
    member this.SetItem(index, item) =
        VersionedList(
            store.SetItem(index, item),
            history.Add(SetItem (index, item)),
            hashCodes.Add(this.GetHashCode()))
    member this.Replace(oldValue, newValue) =
        VersionedList(
            store.Replace(oldValue, newValue),
            history.Add(Replace (oldValue, newValue)),
            hashCodes.Add(this.GetHashCode()))

    interface IReadOnlyList<'T> with
        member this.GetEnumerator() : IEnumerator<'T> = store.GetEnumerator()
        member this.GetEnumerator() : System.Collections.IEnumerator = store.GetEnumerator()
        member this.Count = store.Count
        member this.Item with get index = store[index]
