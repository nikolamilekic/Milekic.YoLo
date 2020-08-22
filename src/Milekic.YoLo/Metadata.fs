module Milekic.YoLo.Metadata

open System.Reflection

let getCallingAssemblyInformationalVersion() =
    let informationalVersion =
        Assembly
            .GetCallingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            .InformationalVersion

    informationalVersion.Substring(0, informationalVersion.IndexOf("+") + 8)
