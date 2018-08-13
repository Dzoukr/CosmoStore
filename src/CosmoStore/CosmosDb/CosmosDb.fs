module CosmoStore.CosmosDb

open System
open System.Threading.Tasks
open Newtonsoft.Json.Linq
open Microsoft.Azure.Documents
open Microsoft.Azure.Documents.Client


let client = new DocumentClient(Uri(""), "")


let proc = new StoredProcedure()