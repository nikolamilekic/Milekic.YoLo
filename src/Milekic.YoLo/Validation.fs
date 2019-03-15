module Milekic.YoLo.Validation

type ValidateRangeError = ValueIsTooSmall | ValueIsTooBig

let inline validateRange< ^a, 'b, 'c when ^a : (static member Maximum : 'b) and
                                          ^a : (static member Minimum : 'b) and
                                          'b : comparison>
    (ctor : 'b -> ^a) x =
    let minimum = (^a : (static member Minimum : 'b) ())
    let maximum = (^a : (static member Maximum : 'b) ())
    if x < minimum then Error ValueIsTooSmall
    elif x > maximum then Error ValueIsTooBig
    else Ok <| ctor x
