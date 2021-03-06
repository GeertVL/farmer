[<AutoOpen>]
module Farmer.Builders.Storage

open Farmer
open Farmer.CoreTypes
open Farmer.Storage
open Farmer.Arm.Storage
open BlobServices
open FileShares

let internal buildKey (ResourceName name) =
    sprintf
        "concat('DefaultEndpointsProtocol=https;AccountName=%s;AccountKey=', listKeys('%s', '2017-10-01').keys[0].value)"
            name
            name
    |> ArmExpression.create

type ShareQuotaInGb = int

type StorageAccountConfig =
    { /// The name of the storage account.
      Name : ResourceName
      /// The sku of the storage account.
      Sku : Sku
      /// Containers for the storage account.
      Containers : (ResourceName * StorageContainerAccess) list
      /// File shares
      FileShares: (ResourceName * ShareQuotaInGb option) list
      /// Queues
      Queues : ResourceName Set }
    /// Gets the ARM expression path to the key of this storage account.
    member this.Key = buildKey this.Name
    member this.Endpoint = sprintf "%s.blob.core.windows.net" this.Name.Value
    interface IBuilder with
        member this.DependencyName = this.Name
        member this.BuildResources location = [
            { Name = this.Name
              Location = location
              Sku = this.Sku }
            for name, access in this.Containers do
                { Name = name
                  StorageAccount = this.Name
                  Accessibility = access }
            for (name, shareQuota) in this.FileShares do
                { Name = name
                  ShareQuota = shareQuota
                  StorageAccount = this.Name }
            for queue in this.Queues do
                { Queues.Queue.Name = queue
                  Queues.Queue.StorageAccount = this.Name }
        ]

type StorageAccountBuilder() =
    member _.Yield _ = { Name = ResourceName.Empty; Sku = Standard_LRS; Containers = []; FileShares = []; Queues = Set.empty }
    member _.Run(state:StorageAccountConfig) =
        { state with
            Name = state.Name |> Helpers.sanitiseStorage |> ResourceName }
    /// Sets the name of the storage account.
    [<CustomOperation "name">]
    member _.Name(state:StorageAccountConfig, name) = { state with Name = name }
    member this.Name(state:StorageAccountConfig, name) = this.Name(state, ResourceName name)
    /// Sets the sku of the storage account.
    [<CustomOperation "sku">]
    member _.Sku(state:StorageAccountConfig, sku) = { state with Sku = sku }
    static member private AddContainer(state, access, name) =
        { state with Containers = state.Containers @ [ (ResourceName name, access) ] }
    /// Adds private container.
    [<CustomOperation "add_private_container">]
    member _.AddPrivateContainer(state:StorageAccountConfig, name) = StorageAccountBuilder.AddContainer(state, Private, name)
    /// Adds container with anonymous read access for blobs and containers.
    [<CustomOperation "add_public_container">]
    member _.AddPublicContainer(state:StorageAccountConfig, name) =  StorageAccountBuilder.AddContainer(state, Container, name)
    /// Adds container with anonymous read access for blobs only.
    [<CustomOperation "add_blob_container">]
    member _.AddBlobContainer(state:StorageAccountConfig, name) = StorageAccountBuilder.AddContainer(state, Blob, name)
    [<CustomOperation "add_file_share">]
    member _.AddFileShare(state:StorageAccountConfig, name) = { state with FileShares = state.FileShares @ [ ResourceName name, None ]}
    [<CustomOperation "add_file_share_with_quota">]
    member _.AddFileShareWithQuota(state:StorageAccountConfig, name, quota) = { state with FileShares = state.FileShares @ [ ResourceName name, Some quota ]}
    [<CustomOperation "add_queue">]
    member _.AddQueue(state:StorageAccountConfig, name) = { state with Queues = state.Queues.Add (ResourceName name) }
    [<CustomOperation "add_queues">]
    member this.AddQueues(state:StorageAccountConfig, names) =
        (state, names) ||> Seq.fold(fun state name -> this.AddQueue(state, name))

/// Allow adding storage accounts directly to CDNs
type EndpointBuilder with
    member this.Origin(state:EndpointConfig, storage:StorageAccountConfig) =
        let state = this.Origin(state, storage.Endpoint)
        this.DependsOn(state, storage.Name)

let storageAccount = StorageAccountBuilder()