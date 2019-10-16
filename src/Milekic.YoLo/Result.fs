module Milekic.YoLo.Result

open Result
open System

let either ok error = function | Ok x -> ok x | Error x -> error x
let liftOption error = function | Some x -> Ok x
                                | None -> Error error
let liftChoice = function | Choice1Of2 x -> Ok x
                          | Choice2Of2 error -> Error error
let toChoice e = e |> either Choice1Of2 Choice2Of2
let isOk e = either (fun _ -> true) (fun _ -> false) e
let isError e = isOk e |> not
let defaultWith f = either id f
let defaultValue x = defaultWith (fun _ -> x)
let failOnError message = defaultWith <| fun _ -> failwith message
let traverse f (source : _ seq) =
    use enumerator = source.GetEnumerator()
    let rec inner state =
        if enumerator.MoveNext() = false then Ok (state |> List.rev) else
        match f enumerator.Current with
        | Error x -> Error x
        | Ok x -> inner (x::state)
    inner []
let sequence source = traverse id source

module Operators =
    let inline (>>=) e f = bind f e
    let inline (>=>) f1 f2 e = f1 e >>= f2
    let inline (>>-) e f = map f e
    let inline (>>-!) x f = Result.mapError f x
    let inline (>>-!.) x error = Result.mapError (fun _ -> error) x
    let inline (>>-.) x value = Result.map (fun _ -> value) x

type Builder() =
    member __.Bind(e, f) = bind f e
    member __.Return x = Ok x
    member __.ReturnFrom x = x
    member __.Zero () = Ok ()
    member __.Delay f = f
    member __.Run f = f()
    member __.TryWith(f, handler) = try f() with e -> handler e
    member __.TryFinally(f, compensation) = try f() finally compensation()
    member this.Using(d : #IDisposable, f) =
        this.TryFinally((fun () -> f d), d.Dispose)
