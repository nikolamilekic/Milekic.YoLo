[<RequireQualifiedAccess>]
module Milekic.YoLo.Metadata

open System
open System.Reflection

let entryAssembly = lazy Assembly.GetEntryAssembly()

let entryAssemblyInformationalVersion =
    lazy
    entryAssembly.Value.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    |> Option.ofObj

let entryAssemblyProduct =
    lazy
    entryAssembly.Value.GetCustomAttribute<AssemblyProductAttribute>()
    |> Option.ofObj

let entryAssemblyReleaseDate =
    lazy
    entryAssembly.Value.GetCustomAttributes<AssemblyMetadataAttribute>()
    |> Seq.tryFind(fun attribute -> attribute.Key = "ReleaseDate")

/// Returns the entry assembly's product together with the version and release date in the following format:
/// {PRODUCT} Version: {VERSION} ({RELEASE DATE})
let productDescription =
    lazy
    entryAssemblyProduct.Value
    |> Option.bind (fun p ->
        entryAssemblyInformationalVersion.Value |> Option.map (fun v -> (p, v)))
    |> Option.map (fun (productAttribute, versionAttribute) ->
        let product = productAttribute.Product
        let version =
            let v = versionAttribute.InformationalVersion
            if v.Contains("+") then v.Substring(0, v.IndexOf("+") + 8) else v
        let dateMaybe =
            entryAssemblyReleaseDate.Value
            |> Option.bind (fun dateAttribute ->
                match DateTimeOffset.TryParse(dateAttribute.Value) with
                | true, date -> Some (date.ToString("yyyy-MM-dd"))
                | _ -> None)
        match dateMaybe with
        | Some dateAsString -> $"{product} Version: {version} ({dateAsString})"
        | None -> $"{product} Version: {version}")
    |> Option.defaultValue ""
