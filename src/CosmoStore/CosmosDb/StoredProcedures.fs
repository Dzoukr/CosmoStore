module CosmoStore.CosmosDb.StoredProcedures

let appendEvent = """
function storedProcedure(streamId, documentToCreate, expectedPosition) {
    
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

    function postSaveActions(err, metadata) {
        if (err) throw new Error("Error" + err.message);
        response.setBody(metadata.position);
    }
    
    function getMetadata(metadataResults) {
        if (metadataResults.length == 0) {
            return {
                streamId:metadataId,
                position:0
            }
        } else {
            return metadataResults[0];
        }
    }

    // append event
    function createDocument(err, metadataResults) {
        if (err) throw new Error("Error" + err.message);
        var metadata = getMetadata(metadataResults);
        var nextPosition = metadata.position + 1;
        checkPosition(nextPosition);
        
        var doc = {
            "id" : documentToCreate.id,
            "correlationId" : documentToCreate.correlationId,
            "streamId" : streamId,
            "position" : nextPosition,
            "name" : documentToCreate.name,
            "data" : documentToCreate.data,
            "metadata" : documentToCreate.metadata,
            "createdUtc" : documentToCreate.createdUtc
        }
        
        collection.createDocument(collection.getSelfLink(), doc, function(err, createdDoc){
            if (err) throw new Error("Error" + err.message);
            metadata.position = nextPosition;
            if (metadata._self) {
                collection.replaceDocument(metadata._self, metadata, postSaveActions);
            } else {
                collection.createDocument(collection.getSelfLink(), metadata, postSaveActions);
            }
        });
    }
    
    // metadata query
    var metadataQuery = 'SELECT * FROM Events e WHERE e.streamId = "'+ metadataId + '"';
    var transactionAccepted = collection.queryDocuments(collection.getSelfLink(), metadataQuery, createDocument);
    if (!transactionAccepted) throw "Transaction not accepted, rollback";
}
"""