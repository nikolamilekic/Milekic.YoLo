namespace Milekic.YoLo.Tests

open Fs1PasswordConnect
open Swensen.Unquote
open FSharpPlus
open FSharpPlus.Data
open FSharpPlus.Lens
open TechTalk.SpecFlow
open Fs1PasswordConnect.ConnectClient
open Milekic.YoLo

[<Binding>]
type ConnectClientFixture() =
    let mutable vaults : VaultInfo list = []
    let mutable items : Item list = []
    let connectClient =
        let getVaults () : ConnectClientMonad<VaultInfo list> = vaults |> result
        let getVaultId (title : VaultTitle) : ConnectClientMonad<VaultId> =
            match vaults |> List.tryFind (fun v -> v.Title = title) with
            | Some v -> v.Id |> result
            | None -> Error VaultNotFound |> ResultT.hoist
        let getItemId (vaultId : VaultId) (itemTitle : ItemTitle) : ConnectClientMonad<ItemId> =
            match vaults |> List.tryFind (fun v -> v.Id = vaultId) with
            | Some _ ->
                match items |> List.tryFind (fun i -> i.Title = itemTitle && i.VaultId = vaultId) with
                | Some i -> result i.Id
                | None -> Error ItemNotFound |> ResultT.hoist
            | None -> Error VaultNotFound |> ResultT.hoist
        let getItem (vaultId : VaultId) (itemId : ItemId) : ConnectClientMonad<Item> =
            match vaults |> List.tryFind (fun v -> v.Id = vaultId) with
            | Some _ ->
                match items |> List.tryFind (fun i -> i.Id = itemId && i.VaultId = vaultId) with
                | Some i -> result i
                | None -> Error ItemNotFound |> ResultT.hoist
            | None -> Error VaultNotFound |> ResultT.hoist
        let getItems (vaultId : VaultId) : ConnectClientMonad<ItemInfo list> =
            match vaults |> List.tryFind (fun v -> v.Id = vaultId) with
            | Some _ ->
                items
                |> List.filter (fun i -> i.VaultId = vaultId)
                |> List.map (fun { Item.Id = id; Title = title; VaultId = vid} ->
                    { Id =id; Title = title; VaultId = vid })
                |> result
            | None -> Error VaultNotFound |> ResultT.hoist

        {
            GetVaults = getVaults
            GetVaultId = getVaultId
            GetItemId = getItemId
            GetItem = getItem
            GetItems = getItems
        }

    let mutable result : Result<_, ConnectClient.InjectError>= Ok ""

    let [<Given>] ``vault with id "(.*)" and title "(.*)"`` id title =
        let vault = { Id = VaultId id; Title = VaultTitle title; }
        vaults <- vault::vaults
    let [<Given>] ``item with id "(.*)" and title "(.*)" in vault "(.*)" with fields`` id title vault (table : Table) =
        let fields =
            table.Rows
            |> Seq.map (fun (row : TableRow) -> {
                Id = FieldId row.[0]
                Label = FieldLabel row.[1]
                Value = FieldValue row.[2]
            })
            |> Seq.toList
        let item = {
            Id = ItemId id
            Title = ItemTitle title
            VaultId = VaultId vault
            Fields = fields
        }
        items <- item::items
    let [<When>] ``the user runs inject with the following text`` text =
        result <- ConnectClient.inject connectClient text |> Async.RunSynchronously
    let [<Then>] ``the result should be`` expected = result =! (Ok expected)
