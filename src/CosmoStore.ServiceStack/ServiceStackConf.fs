namespace CosmoStore.ServiceStack

open ServiceStack.Data
open ServiceStack.Text

type Configuration = {
    Factory: IDbConnectionFactory
    Serializer: IStringSerializer
}
