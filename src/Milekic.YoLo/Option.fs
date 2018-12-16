namespace Milekic.YoLo

module Seq =
    let onlySome xs = seq {
        for x in xs do match x with | Some v -> yield v | None -> ()
    }

module List =
    let onlySome list =
        List.foldBack
            (fun x xs -> match x with | None -> xs | Some v -> v::xs )
            list
            []
