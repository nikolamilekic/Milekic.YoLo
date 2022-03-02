module Milekic.YoLo.Tests.BaseConverterTests

open System
open System.Numerics
open Expecto
open FsCheck
open Swensen.Unquote

open Milekic.YoLo

let toCustomBase = BaseConverter.toCustomBase
let fromCustomBase = BaseConverter.fromCustomBase

[<Tests>]
let baseConverterTests = testList "BaseConverter" [
    testList "Roundtrip tests" [
        let roundtrip b a = toCustomBase b a |> fromCustomBase b =! a
        let roundtripProperty b a = roundtrip (b + 1) a

        testProperty "Roundtrip produces the same array" <| fun (PositiveInt b) ->
            roundtripProperty b
        testProperty "Roundtrip with empty array" <| fun (PositiveInt b) ->
            roundtripProperty b [||]
        testProperty "Roundtrip with a single 0" <| fun (PositiveInt b) ->
            roundtripProperty b [|0uy|]
        testProperty "Roundtrip with double 0" <| fun (PositiveInt b) ->
            roundtripProperty b [|0uy; 0uy|]
        testProperty "Roundtrip with triple 0" <| fun (PositiveInt b) ->
            roundtripProperty b [|0uy; 0uy; 0uy|]
        testCase "Roundtrip with a single 0 base 5" <| fun () ->
            roundtrip 5 [|0uy|]
        testCase "Roundtrip with a single 255 base 5" <| fun () ->
            roundtrip 5 [|255uy|]
        testCase "Roundtrip with a single 0 base 2" <| fun () ->
            roundtrip 2 [|0uy|]
        testCase "Roundtrip with a single 0 base 3" <| fun () ->
            roundtrip 3 [|0uy|]
        testCase "Roundtrip with two 255s and base 65536" <| fun () ->
            roundtrip 65536 [|255uy; 255uy|]
        testCase "Roundtrip with 1 and base 2" <| fun () ->
            roundtrip 2 [|1uy|]
        testCase "Roundtrip with 6 0s and base 16" <| fun () ->
            roundtrip 16 [|0uy; 0uy; 0uy; 0uy; 0uy; 0uy|]
        testCase "Roundtrip with base 2 input 2" <| fun () ->
            roundtrip 2 [|1uy; 0uy|]
    ]

    testList "toCustomBase" [
        testCase "Fails when base is 0" <| fun () ->
            Expect.throws (fun () -> toCustomBase 0 [|1uy|] |> ignore) "Should throw when base is 0"
        testCase "Fails when base is 1" <| fun () ->
            Expect.throws (fun () -> toCustomBase 1 [|1uy|] |> ignore) "Should throw when base is 1"
        testProperty "Fails when base is < 0" <| fun (NegativeInt x) ->
            Expect.throws (fun () -> toCustomBase x [|1uy|] |> ignore) "Should throw when base is negative"
        testCase "Two 255s and base 65536" <| fun () ->
            toCustomBase 65536 [|255uy; 255uy|] =! [|65535|]
        testCase "67 base 50" <| fun () ->
            toCustomBase 50 [|67uy|] =! [|17; 1|]

        let ``Number has equal value when converted back to decimal`` (PositiveInt b) (number : bigint) =
            let expected = if number < 0I then number * -1I else number
            let customBase = expected.ToByteArray() |> toCustomBase (b + 1)
            let b = bigint (b + 1)
            let actual =
                customBase
                |> Seq.foldBack (fun digit sum -> sum * b + bigint digit) <| 0I
            actual =! expected

        testProperty "Number has equal value when converted back to decimal" <|
            ``Number has equal value when converted back to decimal``
        testProperty "Number has equal value when converted back to decimal (numbers * 100)" <| fun b n ->
            ``Number has equal value when converted back to decimal`` b (n * 100I)
        testProperty "Number has equal value when converted back to decimal (numbers * 10000)" <| fun b n ->
            ``Number has equal value when converted back to decimal`` b (n * 10000I)
        testCase "Number has equal value when converted back to decimal base 2 input 200" <| fun () ->
            ``Number has equal value when converted back to decimal`` (PositiveInt 1) 200I
        testProperty "1 has equal value when converted back from base 2" <| fun () ->
            let expected = 1I
            let customBase = expected.ToByteArray() |> toCustomBase 2
            let actual =
                customBase
                |> Seq.foldBack (fun digit sum -> sum * 2I + bigint digit) <| 0I
            actual =! expected
        testCase "500 is correctly converted to base 2" <| fun () ->
            let expected = [| 0;0;1;0; 1;1;1;1; 1;0;0;0; 0;0;0;0; |]
            let actual = toCustomBase 2 [|244uy; 1uy|]
            actual =! expected
        testCase "200 is correctly converted to base 2" <| fun () ->
            let expected = [| 0;0;0;1; 0;0;1;1 |]
            let actual = toCustomBase 2 [|200uy|]
            actual =! expected
    ]

    testList "fromCustomBase" [
        testCase "Fails when base is 0" <| fun () ->
            raisesWith<ArgumentOutOfRangeException>
                <@ fromCustomBase 0 [|1|] @>
                (fun e -> <@ e.ParamName = "base" @>)
        testCase "Fails when base is 1" <| fun () ->
            raisesWith<ArgumentOutOfRangeException>
                <@ fromCustomBase 1 [|1|] @>
                (fun e -> <@ e.ParamName = "base" @>)
        testProperty "Fails when base is < 0" <| fun (NegativeInt x) ->
            raisesWith<ArgumentOutOfRangeException>
                <@ fromCustomBase x [|1|] @>
                (fun e -> <@ e.ParamName = "base" @>)
        testCase "Fails when a number is equal than base" <| fun () ->
            raisesWith<ArgumentOutOfRangeException>
                <@ fromCustomBase 2 [|2|] @>
                (fun e -> <@ e.ParamName = "number" @>)
        testCase "Fails when a number is bigger than base" <| fun () ->
            raisesWith<ArgumentOutOfRangeException>
                <@ fromCustomBase 2 [|3|] @>
                (fun e -> <@ e.ParamName = "number" @>)
        testCase "Two 65535 and base 256" <| fun () ->
            fromCustomBase 65536 [|65535|] =! [|255uy; 255uy|]
        testCase "67 base 50" <| fun () ->
            fromCustomBase 50 [|17; 1|] =! [|67uy|]
        testCase "61695 base 70000" <| fun () ->
            fromCustomBase 70000 [|61695|] =! [|255uy; 240uy|]

        let ``Number has equal value when converted back to decimal`` (PositiveInt b) (PositiveInt number) =
            let expected = bigint number
            let inB = toCustomBase (b + 1) (expected.ToByteArray())
            let result = fromCustomBase (b + 1) inB
            let actual =
                result
                |> Seq.foldBack (fun digit sum -> sum * 256I + bigint digit) <| 0I
            actual =! expected

        testProperty "Number has equal value when converted back to decimal" <|
            ``Number has equal value when converted back to decimal``
        testProperty "Number has equal value when converted back to decimal (number * 100)" <| fun b (PositiveInt number) ->
            ``Number has equal value when converted back to decimal`` b (PositiveInt (number * 100))
        testProperty "Number has equal value when converted back to decimal (number * 10000)" <| fun b (PositiveInt number) ->
            ``Number has equal value when converted back to decimal`` b (PositiveInt (number * 10000))
        testCase "Number has equal value when converted back to decimal base 2 input 200" <| fun () ->
            ``Number has equal value when converted back to decimal`` (PositiveInt 1) (PositiveInt 200)
        testCase "1 has equal value when converted back to decimal" <| fun () ->
            let actual =
                fromCustomBase 2 [|1|]
                |> Seq.foldBack (fun digit sum -> sum * 256I + bigint digit) <| 0I
            actual =! 1I
        testCase "81 x 31" <| fun () ->
            Array.replicate 81 31 |> fromCustomBase 32 |> ignore
        testCase "2 x 31" <| fun () ->
            Array.replicate 2 31 |> fromCustomBase 32 |> ignore
    ]
]
