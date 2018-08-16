module CosmoStore.CosmosDb.StoredProcedures

let appendEvent = """
function storedProcedure(streamId, documentsToCreate, expectedPosition) {
    
    var context = getContext();
    var collection = context.getCollection();
    var response = context.getResponse();
    var metadataId = '$_'+streamId;

    function checkPosition(nextPosition) {
        if (expectedPosition.mode == "any") {
            return;
        }
        if (expectedPosition.mode == "noStream" && nextPosition > 0) {
            throw "ESERROR_POSITION_STREAMEXISTS";
        }
        if (expectedPosition.mode == "exact" && nextPosition != expectedPosition.position) {
            throw "ESERROR_POSITION_POSITIONNOTMATCH";
        }
    }
    
    // append event
    function createDocument(err, metadata) {
        if (err) throw new Error("Error" + err.message);
        checkPosition(metadata.position + 1);
        var nextPosition = metadata.position;

        var resp = [];                
        for(var i in documentsToCreate) {
            nextPosition++;
            var d = documentsToCreate[i];
            var created = new Date().toISOString();
            var doc = {
                "id" : d.id,
                "correlationId" : d.correlationId,
                "streamId" : streamId,
                "position" : nextPosition,
                "name" : d.name,
                "data" : d.data,
                "metadata" : d.metadata,
                "createdUtc" : created
            }
            
            resp.push({ position: nextPosition, created : created });
            collection.createDocument(collection.getSelfLink(), doc, function(err, __){
                if (err) throw new Error("Error" + err.message);
                metadata.position = nextPosition;
                metadata.lastUpdated = created;
                collection.replaceDocument(metadata._self, metadata, function(err,__){
                    if (err) throw new Error("Error" + err.message);
                    response.setBody(resp);
                });
            });
            
        }
    }
    
    // main function
    function run(err,metadataResults) {
        if (metadataResults.length == 0) {
            let newMeta = {
                streamId:metadataId,
                position:0
            }
            return collection.createDocument(collection.getSelfLink(), newMeta, createDocument)

        } else {
            return createDocument(err, metadataResults[0]);
        }
    }

    // metadata query
    var metadataQuery = 'SELECT * FROM Events e WHERE e.streamId = "'+ metadataId + '"';
    var transactionAccepted = collection.queryDocuments(collection.getSelfLink(), metadataQuery, run);
    if (!transactionAccepted) throw "Transaction not accepted, rollback";
}
"""