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
    let inline run state =
        let rec inner state = function
            | Pure x -> Ok (x, state)
            | Free x ->
                let next, nextState = Update.run state x
                match next with
                | Ok x -> inner nextState x
                | Error e -> Error e
        inner state

    module Operators =
        let inline (>>=) e f = bind f e
        let inline (>=>) f1 f2 e = f1 e >>= f2
        let inline (>>-) e f = map f e
        let inline (>>-!) x f = mapError f x
        let inline (>>-!.) x error = mapError (fun _ -> error) x
        let inline (>>-.) x value = map (fun _ -> value) x

    open Operators

    let inline traverse f source =
        let folder state element = state >>= (fun head ->
                                   f element >>= (fun tail ->
                                   liftValue (tail::head)))
        List.fold folder (liftValue []) source
        >>- List.rev
    let inline sequence source = traverse id source

    type Builder() =
        member inline __.Bind(e, f) = bind f e
        member inline __.Return x = liftValue x
        member __.ReturnFrom x = x
        member inline __.Zero () = liftValue ()
        member inline __.Delay f = delay f
        member __.Run f = f
        member __.TryWith(f, handler) = try f() with e -> handler e
        member __.TryFinally(f, compensation) = try f() finally compensation()
        member this.Using(d : #IDisposable, f) =
            this.TryFinally((fun () -> f d), d.Dispose)
