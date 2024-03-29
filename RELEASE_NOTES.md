## New in 3.4 (Released 2022/10/03)
* Go back to using Fs1PasswordConnect. The official action does not work on Windows.

## New in 3.3 (Released 2022/09/09)
* Build script: Switched to 1password/load-secrets-action
* Build script: Xml and pdb files are now excluded from publish output

## New in 3.2 (Released 2022/03/11)
* Targets expanded to include versioning properties for MacOS app bundles

## New in 3.1 (Released 2022/03/02)
* BaseConverter

## New in 3.0 (Released 2022/02/20)
* Switch to .NET 6
* Metadata is now automatically added to any referencing assemblies (if they also depend on GitInfo)
* Symbols are now published as a separate nuget package (snupkg)

## New in 2.2 (Released 2020/12/13)
* Now targeting .NET 5

## New in 2.1 (Released 2020/09/10)
* Regex partial active pattern
* Update and UpdateResult improvements
* Functions and modules that overlap FSharpPlus are marked as obsolete
* Extended nameOf

## New in 2.0 (Released 2020/08/09)
* (Breaking change) Update.run signature changed
* (Breaking change) SimpleUpdate.read moved to Update module
* (Breaking change) SimpleUpdate.get is now Update.getState
* Add nameOf, instanceOf operators
* Range validation
* Additional Result operators, sequence function and Builder methods
* UpdateResult
* Improved TryWith, TryFinally and Using Builder methods
* Add SourceLink support

## New in 1.0 (Released 2018/12/16)
* Un/curry and flip
* atomicUpdateQuery
* List, Seq onlySome

## New in 0.1 (Released 2018/04/27)
* Initial release
