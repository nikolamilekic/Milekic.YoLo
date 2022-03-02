[<RequireQualifiedAccess>]
module Milekic.YoLo.BaseConverter

open System
open System.Numerics

/// Converts a number into a custom base and outputs the individual digits
/// This function expects number to be in the little endian byte order,
/// and the returned digits will be in the little endian order as well
let toCustomBase (``base`` : int) (number : byte array) : int array =
    if ``base`` <= 1 then raise (ArgumentOutOfRangeException((nameof ``base``), "base cannot be <= 1"))

    if number.Length = 0 then [||] else

    let initial =
        if number[number.Length - 1] > 127uy then
            //Turning this into a BigInteger would result in a negative number.
            //Need to flip the sign.
            Array.append number [|0uy|]
        else number
        |> BigInteger

    let divisor = bigint ``base``
    let rec inner (current : BigInteger) = seq {
        if current <> 0I then
            let n, r = BigInteger.DivRem(current, divisor)
            if r < 0I then yield r + divisor else yield r
            yield! inner n
    }

    let bigIntegerResult, length =
        let x = inner initial |> Seq.toArray
        if x.Length = 0 then Seq.empty, 0 else
        if x |> Array.last = 0I
        then Seq.truncate (x.Length - 1) x, x.Length - 1
        else x, x.Length

    let expectedLength =
        float number.Length * Math.Log(256, ``base``)
        |> Math.Ceiling
        |> int

    let missingDigits = expectedLength - length

    seq { bigIntegerResult; Seq.replicate missingDigits 0I }
    |> Seq.concat
    |> Seq.map int
    |> Seq.toArray

/// Converts a number from a custom base into base 256 (byte array)
/// This function expects number to be in the little endian order,
/// and the returned bytes will be in the little endian byte order as well
let fromCustomBase (``base`` : int) (number : int array) : byte array =
    if ``base`` <= 1 then raise (ArgumentOutOfRangeException((nameof ``base``), "base cannot be <= 1"))
    if Seq.exists ((<=) ``base``) number then raise (ArgumentOutOfRangeException((nameof number), "A digit in number cannot be >= base"))

    if number.Length = 0 then [||] else

    let divisor = bigint ``base``
    let bigIntResult =
        Seq.foldBack
            (fun (current : int) sum -> sum * divisor + (bigint current))
            number
            0I

    let bigIntArray = bigIntResult.ToByteArray()

    let minimumBytes =
        float number.Length * Math.Log(``base``, 256)
        |> Math.Floor
        |> int

    if minimumBytes = bigIntArray.Length then bigIntArray else

    let bigIntArray, bigIntArrayLength =
        if bigIntArray |> Seq.last = 0uy
        then bigIntArray |> Seq.take (bigIntArray.Length - 1), (bigIntArray.Length - 1)
        else bigIntArray, bigIntArray.Length

    let missingBytes = minimumBytes - bigIntArrayLength |> max 0

    seq { bigIntArray; Seq.replicate missingBytes 0uy }
    |> Seq.concat
    |> Seq.toArray
