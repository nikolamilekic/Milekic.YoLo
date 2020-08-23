namespace Milekic.YoLo

#nowarn "40" "44"

open System

[<NoComparison; NoEquality; Obsolete("Use FSharpPlus instead")>]
type UpdateResult<'s, 'u, 'a, 'e> =
    | Pure of 'a
    | Free of Update<'s, 'u, Result<UpdateResult<'s, 'u, 'a, 'e>, 'e>>

[<Obsolete("Use FSharpPlus instead")>]
module UpdateResult =
    let inline private mapStack f = Update.map (Result.map f)
    let inline bind f =
        let rec inner = function
            | Pure x -> f x
            | Free x -> x |> mapStack inner |> Free
        inner
    let inline map f = (f >> Pure) |> bind
    let inline private wrap x = x |> mapStack Pure |> Free
    let inline liftResult x = Update.liftValue x |> wrap
    let inline liftValue x = Ok x |> liftResult
    let inline liftError x = Error x |> liftResult
    let inline liftUpdate x = Update.map Ok x |> wrap
    let inline read f = Update.read f |> liftUpdate
    let inline mapError f =
        let rec inner = function
            | Pure x -> Pure x
            | Free x ->
                let f1 = function
                    | Ok x -> inner x |> Ok
                    | Error x -> Error (f x)
                Update.map f1 x |> Free
        inner
    let inline delay f = liftValue () |> bind f
    let inline runWithUpdate state =
        let rec inner (state, update) = function
            | Pure x -> Ok (update, (x, state))
            | Free x ->
                let extraUpdate, (result, state) = Update.runWithUpdate state x
                let update = Update.combine(update, extraUpdate)
                match result with
                | Ok x -> inner (state, update) x
                | Error e -> Error e
        inner (state, Update.unit)
    let inline run state = runWithUpdate state >> Result.map snd

    [<Obsolete("Use FSharpPlus instead")>]
    module Operators =
        let inline (>>=) e f = bind f e
        let inline (>>=.) e1 e2 = e1 |> bind (fun _ -> e2)
        let inline (>=>) f1 f2 e = f1 e >>= f2
        let inline (>>-) e f = map f e
        let inline (>>-!) x f = mapError f x
        let inline (>>-!.) x error = mapError (fun _ -> error) x
        let inline (>>-.) x value = map (fun _ -> value) x

    [<Obsolete("Use FSharpPlus instead")>]
    type Builder() =
        member inline __.Bind(e, f) = bind f e
        member inline __.Return x = liftValue x
        member __.ReturnFrom x = x
        member inline __.Zero () = liftValue ()
        member inline __.Delay f = delay f
        member __.Run f = f
        member inline __.TryWith(e, handler) =
            fun state ->
                try match runWithUpdate state e with
                    | Ok (u, (x, _)) -> u, (Ok x)
                    | Error e -> Update.unit, (Error e)
                with e ->
                    match runWithUpdate state (handler e) with
                    | Ok (u, (x, _)) -> u, (Ok x)
                    | Error e -> Update.unit, (Error e)
            |> Update
            |> wrap
        member inline __.TryFinally(e, compensation) =
            fun state -> try match runWithUpdate state e with
                             | Ok (u, (x, _)) -> u, (Ok x)
                             | Error e -> Update.unit, (Error e)
                         finally compensation()
            |> Update
            |> wrap
        member inline this.Using(d : #IDisposable, f) =
            this.TryFinally(delay (fun () -> f d), d.Dispose)
        member inline __.Combine(eUnit, e) = bind (fun _ -> e) eUnit

    let updateResult = Builder()
    let inline traverse f (source : _ seq) = updateResult {
        use enumerator = source.GetEnumerator()
        let rec inner state = updateResult {
            if enumerator.MoveNext() = false then return List.rev state else
            let! x = f enumerator.Current
            return! inner (x::state)
        }
        let! result = inner []
        return result
    }
    let inline sequence source = traverse id source

    let inline traverseUnit f (source : _ seq) = updateResult {
        use enumerator = source.GetEnumerator()
        let rec inner = updateResult {
            if enumerator.MoveNext() = false then return () else
            do! f enumerator.Current
            return! inner
        }
        return! inner
    }

    let inline sequenceUnit x = traverseUnit id x
