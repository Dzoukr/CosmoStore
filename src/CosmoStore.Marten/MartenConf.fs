namespace CosmoStore.Marten
open Marten

type Configuration = {
    MartenStore : IDocumentStore
}


module Testing =
       [<CLIMutable>]
       type Foo = {
           Bar : string
       }
        