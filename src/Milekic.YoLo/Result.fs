[<RequireQualifiedAccess>]
module Milekic.YoLo.Result

let isOk = function | Ok _ -> true | Error _ -> false
let isError = function | Ok _ -> false | Error _ -> true
let failOnError message = Option.defaultWith <| fun _ -> failwith message
