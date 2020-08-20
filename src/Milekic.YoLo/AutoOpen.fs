[<AutoOpen>]
module Milekic.YoLo.AutoOpen

open System

#nowarn "44"

[<Obsolete("Use FSharpPlus instead")>]
let result = Result.Builder()
[<Obsolete("Use FSharpPlus instead")>]
let update = Update.Builder()
[<Obsolete("Use FSharpPlus instead")>]
let updateResult = UpdateResult.Builder()
[<Obsolete("Use FSharpPlus instead")>]
let option = Option.Builder()
