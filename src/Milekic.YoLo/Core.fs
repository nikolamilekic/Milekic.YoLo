[<AutoOpen>]
module Milekic.YoLo.Core

open System
open System.Text.RegularExpressions
open System.Threading
open System.Collections.Generic
open Microsoft.FSharp.Quotations.Patterns

[<Obsolete("Use FSharpPlus instead")>]
let curry f a b = f(a, b)
[<Obsolete("Use FSharpPlus instead")>]
let uncurry f (a, b) = f a b
[<Obsolete("Use FSharpPlus instead")>]
let flip f a b = f b a
let rec atomicUpdateQuery state update =
    let oldState = !state
    let result, newState = update oldState
    let ok = Interlocked.CompareExchange<_>(state, newState, oldState)
             |> LanguagePrimitives.PhysicalEquality oldState
    if ok then result, newState else atomicUpdateQuery state update
let atomicUpdateQueryResult s u = (s, u) ||> atomicUpdateQuery |> fst

let nameOf = function
    | PropertyGet(_, propertyInfo, _) -> propertyInfo.Name
    | FieldGet(_, fieldInfo) -> fieldInfo.Name
    | Lambda (_, NewUnionCase (info, _)) -> info.Name
    | Lambda (_, Call (_, method, _)) -> method.Name
    | e -> failwithf "Unsupported quotation was passed to nameOf %A" e

let instanceOf<'T> : 'T = failwith "instanceOf operator should only be used in expressions and should never actually be called"

type IDictionary<'a, 'b> with
    member this.TryFind(key) =
        match this.TryGetValue(key) with
        | true, x -> Some x
        | false, _ -> None

let (|Regex|_|) pattern input =
    let m = Regex.Match(input, pattern)
    if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
    else None
