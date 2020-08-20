namespace Milekic.YoLo

#nowarn "44"

open System

[<NoComparison; NoEquality; Obsolete("Use FSharpPlus instead")>]
type Eventually<'a> =
    | Done of 'a
    | NotDone of (unit -> Eventually<'a>)

[<Obsolete("Use FSharpPlus instead")>]
module Eventually =
    let rec bind f = function
        | Done x -> NotDone (fun () -> f x)
        | NotDone inner -> NotDone (fun () -> bind f (inner()))
    let map f = f >> Done |> bind
    let delay f = f >> Done |> NotDone
    let rec run = function | Done x -> x | NotDone next -> next() |> run

    [<Obsolete("Use FSharpPlus instead")>]
    module Operators =
        let inline (>>=) e f = bind f e
        let inline (>=>) f1 f2 e = f1 e >>= f2
        let inline (>>-) e f = map f e
