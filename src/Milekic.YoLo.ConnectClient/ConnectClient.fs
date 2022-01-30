[<RequireQualifiedAccess>]
module Milekic.YoLo.ConnectClient

open FSharpPlus
open FSharpPlus.Data
open Fs1PasswordConnect.ConnectClient
open Milekic.YoLo

type InjectError =
    | ConnectError of ConnectError
    | FieldNotFound
    with
    override this.ToString() =
        match this with
        | ConnectError e -> e.ToString()
        | FieldNotFound -> "Field not found"

let inject (client : ConnectClient) template =
    let client = ConnectClientFacade(cache client)

    let rec inner (template : string) : Async<Result<string, InjectError>> = async {
        match template with
        | Regex "{{ op://(.+)/(.+)/(.+) }}" [ vault; item; field ] ->
            let replacement = "{{ " + $"op://{vault}/{item}/{field}" + " }}"

            let getField (vaultId : VaultId) (itemId : ItemId) = async {
                match! client.GetItem(vaultId, itemId) with
                | Ok item ->
                    let field =
                        item.Fields
                        |> List.tryFind (fun { Id = FieldId id; Label = FieldLabel label } ->
                            id = field || label = field)
                    match field with
                    | Some { Value = FieldValue v } ->
                        return! (template.Replace(replacement, v) |> inner)
                    | None -> return Error FieldNotFound
                | Error e -> return Error (ConnectError e)
            }

            //Assume vault is a title
            match! client.GetVaultId (VaultTitle vault) with
            | Ok vaultId ->
                //Assume item is a title
                match! client.GetItemId (vaultId, ItemTitle item) with
                | Ok itemId -> return! getField vaultId itemId
                | Error ItemNotFound -> return! getField vaultId (ItemId item) //Maybe item is id
                | Error e -> return Error (ConnectError e)
            | _ ->
                //Assume vault is id
                match! client.GetItemId (VaultId vault, ItemTitle item) with
                | Ok itemId -> return! getField (VaultId vault) itemId
                | Error ItemNotFound -> return! getField (VaultId vault) (ItemId item) //Maybe item is id
                | Error e -> return Error (ConnectError e)
        | _ -> return Ok template
    }

    inner template
