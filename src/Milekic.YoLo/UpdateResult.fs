namespace Milekic.YoLo

open System

[<NoComparison; NoEquality>]
type UpdateResult<'s, 'u, 'a, 'e> =
    | Pure of 'a
    | Free of Update<'s, 'u, Result<UpdateResult<'s, 'u, 'a, 'e>, 'e>>

module UpdateResult =
    let inline private mapStack f = Update.map (Result.map f)
    let inline bind f =
        let rec inner = function
            | Pure x -> f x
            | Free x -> x |> mapStack inner |> Free
        inner
    let inline map f = (f >> Pure) |> bind
    let inline private wrap x = x |> mapStack Pure |> Free
    let inline liftValue x = Ok x |> Update.liftValue |> wrap
    let inline liftResult x = Update.liftValue x |> wrap
    let inline liftUpdate x = Update.map Ok x |> wrap
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

    module Operators =
        let inline (>>=) e f = bind f e
        let inline (>=>) f1 f2 e = f1 e >>= f2
        let inline (>>-) e f = map f e
        let inline (>>-!) x f = mapError f x
        let inline (>>-!.) x error = mapError (fun _ -> error) x
        let inline (>>-.) x value = map (fun _ -> value) x

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
