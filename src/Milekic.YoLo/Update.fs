namespace Milekic.YoLo

[<NoComparison; NoEquality>]
type Update<'s, 'u, 'a> = Update of ('s -> 'u * 'a)
module Update =
    let inline unit< ^u when ^u : (static member Unit : ^u)> : ^u =
        (^u : (static member Unit : ^u) ())
    let inline combine< ^u when ^u : (static member Combine : ^u * ^u -> ^u)>
        (a, b) : ^u = (^u : (static member Combine : ^u * ^u -> ^u) (a, b))
    let inline apply< ^s, ^u when ^u : (static member Apply : ^s * ^u -> ^s)>
        (state, update) : ^s =
        (^u : (static member Apply : ^s * ^u -> ^s) (state, update))
    let run state (Update f) = f state
    let inline liftValue x = Update (fun _ -> (unit, x))
    let inline bind f e = fun s0 -> let (u1, r1) = run s0 e
                                    let s1 = apply (s0, u1)
                                    let (u2, r2) = run s1 (f r1)
                                    combine (u1, u2), r2
                          |> Update
    let inline map f = (f >> liftValue) |> bind

    module Operators =
        let inline (>>=) e f = bind f e
        let inline (>>-) e f = map f e

    type Builder() =
        member inline __.Return x = liftValue x
        member __.ReturnFrom x = x
        member inline __.Bind(e, f) = bind f e
        member inline __.Zero() = liftValue ()
        member inline __.Delay(f) = bind f (liftValue ())

[<NoComparison; NoEquality>]
type SimpleUpdate<'s> =
    | DoNothing
    | ApplySimpleUpdate of ('s -> 's)
    static member Apply (s, u) = match u with | DoNothing -> s
                                              | ApplySimpleUpdate f -> f s
    static member Unit : SimpleUpdate<'s> = DoNothing
    static member Combine(a, b) =
        match (a, b) with
        | DoNothing, x
        | x, DoNothing -> x
        | ApplySimpleUpdate a, ApplySimpleUpdate b -> ApplySimpleUpdate (a >> b)

module SimpleUpdate =
    let applyUpdate updateF : Update<'s, SimpleUpdate<'s>, unit> =
        (fun _ -> ApplySimpleUpdate updateF, ()) |> Update
    let read f : Update<'s, SimpleUpdate<_>, _> =
        (fun state -> DoNothing, f state) |> Update
    let get<'s> : Update<'s, SimpleUpdate<'s>, _> = read id
