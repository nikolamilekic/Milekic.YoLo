[<System.Obsolete("Use FSharpPlus instead")>]
module Milekic.YoLo.Validation

#nowarn "44"

open System

[<Obsolete("Use FSharpPlus instead")>]
type ValidateRangeError = ValueIsTooSmall | ValueIsTooBig

[<Obsolete("Use FSharpPlus instead")>]
let inline validateRange< ^a, 'b, 'c when ^a : (static member Maximum : 'b) and
                                          ^a : (static member Minimum : 'b) and
                                          'b : comparison>
    (ctor : 'b -> ^a) x =
    let minimum = (^a : (static member Minimum : 'b) ())
    let maximum = (^a : (static member Maximum : 'b) ())
    if x < minimum then Error ValueIsTooSmall
    elif x > maximum then Error ValueIsTooBig
    else Ok <| ctor x
