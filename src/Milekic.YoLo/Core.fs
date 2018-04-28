[<AutoOpen>]
module Milekic.YoLo.Core

open System.Threading

let curry f a b = f(a, b)
let uncurry f (a, b) = f a b
let flip f a b = f b a
let rec atomicUpdateQuery state update =
    let oldState = !state
    let result, newState = update oldState
    let ok = Interlocked.CompareExchange<_>(state, newState, oldState)
             |> LanguagePrimitives.PhysicalEquality oldState
    if ok then result, newState else atomicUpdateQuery state update
let atomicUpdateQueryResult s u = (s, u) ||> atomicUpdateQuery |> fst
