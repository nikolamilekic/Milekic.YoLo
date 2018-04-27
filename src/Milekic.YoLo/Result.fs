module Milekic.YoLo.Result

open Result

let either ok error = function | Ok x -> ok x | Error x -> error x
let liftChoice = function | Choice1Of2 x -> Ok x
                          | Choice2Of2 error -> Error error
let toChoice e = e |> either Choice1Of2 Choice2Of2
let isOk e = either (fun _ -> true) (fun _ -> false) e
let isError e = isOk e |> not
let defaultWith f = either id f
let defaultValue x = defaultWith (fun _ -> x)
let failOnError message = defaultWith <| fun _ -> failwith message

module Operators =
    let inline (>>=) e f = bind f e
    let inline (>=>) f1 f2 e = f1 e >>= f2
    let inline (>>-) e f = map f e

open Operators

let traverse f source =
    let folder element state = state >>= (fun tail ->
                                f element >>= (fun head ->
                                Ok (head::tail)))
    List.foldBack folder source (Ok [])

type Builder() =
    member __.Bind(e, f) = bind f e
    member __.Return x = Ok x
    member __.ReturnFrom x = x
