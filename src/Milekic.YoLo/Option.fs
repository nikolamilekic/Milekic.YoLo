namespace Milekic.YoLo

open System

module Seq =
    [<Obsolete("Use FSharpPlus chooise id instead")>]
    let onlySome xs = seq {
        for x in xs do match x with | Some v -> yield v | None -> ()
    }

module List =
    [<Obsolete("Use FSharpPlus chooise id instead")>]
    let onlySome list =
        List.foldBack
            (fun x xs -> match x with | None -> xs | Some v -> v::xs )
            list
            []

open Option

[<Obsolete("Use FSharpPlus instead")>]
module Option =
    let traverse f (source : _ seq) =
        use enumerator = source.GetEnumerator()
        let rec inner state =
            if enumerator.MoveNext() = false then Some (state |> List.rev) else
            match f enumerator.Current with
            | None -> None
            | Some x -> inner (x::state)
        inner []

    let sequence source = traverse id source

    module Operators =
        let inline (>>=) e f = bind f e
        let inline (>=>) f1 f2 e = f1 e >>= f2
        let inline (>>-) e f = map f e
        let inline (>>-.) x value = map (fun _ -> value) x

    type Builder() =
        member __.Bind(e, f) = bind f e
        member __.Return x = Some x
        member __.ReturnFrom x = x
        member __.Zero () = Some ()
        member __.Delay f = f
        member __.Run f = f()
        member __.TryWith(f, handler) = try f() with e -> handler e
        member __.TryFinally(f, compensation) = try f() finally compensation()
        member this.Using(d : #IDisposable, f) =
            this.TryFinally((fun () -> f d), d.Dispose)
