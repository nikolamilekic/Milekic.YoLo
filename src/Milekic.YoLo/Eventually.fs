namespace Milekic.YoLo

type Eventually<'a> =
    | Done of 'a
    | NotDone of (unit -> Eventually<'a>)

module Eventually =
    let rec bind f = function
        | Done x -> NotDone (fun () -> f x)
        | NotDone inner -> NotDone (fun () -> bind f (inner()))
    let map f = f >> Done |> bind
    let delay f = f >> Done |> NotDone
    let rec run = function | Done x -> x | NotDone next -> next() |> run

    module Operators =
        let inline (>>=) e f = bind f e
        let inline (>=>) f1 f2 e = f1 e >>= f2
        let inline (>>-) e f = map f e
