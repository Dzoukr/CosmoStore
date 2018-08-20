function storedProcedure(streamId, documentsToCreate, expectedPosition) {

    var context = getContext();
    var collection = context.getCollection();
    var response = context.getResponse();
    var streamType = "Stream";
    var eventType = "Event";

    function checkError(err) {
        if (err) throw new Error("Error : " + err.message);
    }

    function checkErrorFn(err, __) {
        checkError(err);
    }

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
        checkPosition(metadata.lastPosition + 1);
        var nextPosition = metadata.lastPosition;
        var resp = [];
        for (var i in documentsToCreate) {
            nextPosition++;
            var d = documentsToCreate[i];
            var created = new Date().toISOString();
            var doc = {
                "type": eventType,
                "id": d.id,
                "correlationId": d.correlationId,
                "streamId": streamId,
                "position": nextPosition,
                "name": d.name,
                "data": d.data,
                "metadata": d.metadata,
                "createdUtc": created
            }

            resp.push({ position: nextPosition, created: created });
            var acceptedDoc = collection.createDocument(collection.getSelfLink(), doc, checkErrorFn);
            if (!acceptedDoc) {
                throw "Failed to append event on position " + nextPosition + " - Rollback. Please try to increase RU for collection Events.";
            }
        }

        metadata.lastPosition = nextPosition;
        metadata.lastUpdatedUtc = created;
        var acceptedMeta = collection.replaceDocument(metadata._self, metadata, checkErrorFn);
        if (!acceptedMeta) {
            throw "Failed to update metadata for stream - Rollback";
        }
        response.setBody(resp);
    }

    // main function
    function run(err, metadataResults) {
        checkError(err);
        if (metadataResults.length == 0) {
            let newMeta = {
                streamId: streamId,
                type: streamType,
                lastPosition: 0
            }
            return collection.createDocument(collection.getSelfLink(), newMeta, createDocument);
        } else {
            return createDocument(err, metadataResults[0]);
        }
    }

    // metadata query
    var metadataQuery = 'SELECT * FROM Events e WHERE e.streamId = "' + streamId + '" AND e.type = "' + streamType + '"';
    var transactionAccepted = collection.queryDocuments(collection.getSelfLink(), metadataQuery, run);
    if (!transactionAccepted) throw "Transaction not accepted, rollback";
}