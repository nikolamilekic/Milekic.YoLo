[<AutoOpen>]
module Milekic.YoLo.Core

let curry f a b = f(a, b)
let uncurry f (a, b) = f a b
let flip f a b = f b a
