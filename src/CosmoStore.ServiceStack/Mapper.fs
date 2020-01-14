namespace CosmoStore.ServiceStack

open ServiceStack.DataAnnotations
open System
open CosmoStore
open ServiceStack.Text

[<CLIMutable>]
[<Alias("cosmo_streams")>]
type StreamDB =
    { Id: StreamId
      LastVersion: int64
      LastUpdatedUtc: DateTime }

    member x.To: Stream<int64> =
        { Id = x.Id
          LastVersion = x.LastVersion
          LastUpdatedUtc = x.LastUpdatedUtc }

    static member From(x: Stream<int64>): StreamDB =
        { Id = x.Id
          LastVersion = x.LastVersion
          LastUpdatedUtc = x.LastUpdatedUtc }

[<CLIMutable>]
[<Alias("cosmo_events")>]
type EventReadDB =
    { Id: Guid
      CorrelationId: Nullable<Guid>
      CausationId: Nullable<Guid>
      StreamId: StreamId
      Version: int64
      Name: string
      Data: string
      Metadata: string
      CreatedUtc: DateTime }



    member this.To<'payload>(serializer: IStringSerializer): EventRead<'payload, int64> =
        { Id = this.Id
          CorrelationId = Option.ofNullable this.CorrelationId
          CausationId = Option.ofNullable this.CausationId
          StreamId = this.StreamId
          Version = this.Version
          Name = this.Name
          Data = serializer.DeserializeFromString<'payload> this.Data
          Metadata =
              if isNull this.Metadata then None
              else Some(serializer.DeserializeFromString<'payload> this.Metadata)
          CreatedUtc = this.CreatedUtc }


    //
    static member From (serializer: IStringSerializer) (x: EventRead<'payload, int64>): EventReadDB =
        { Id = x.Id
          CorrelationId = Option.toNullable x.CorrelationId
          CausationId = Option.toNullable x.CausationId
          StreamId = x.StreamId
          Version = x.Version
          Name = x.Name
          Data = serializer.SerializeToString<'payload> x.Data
          Metadata =
              match x.Metadata with
              | Some a -> serializer.SerializeToString a
              | None -> serializer.SerializeToString null
          CreatedUtc = x.CreatedUtc }
