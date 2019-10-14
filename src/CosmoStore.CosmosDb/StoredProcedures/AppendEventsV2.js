function storedProcedure(streamId, documentsToCreate, expectedPosition) {

    var context = getContext();
    var container = context.getCollection();
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
        if (expectedPosition.mode == "noStream" && nextPosition > 1) {
            throw "ESERROR_VERSION_STREAMEXISTS [nextVersion="+nextPosition+"]";
        }
        if (expectedPosition.mode == "exact" && nextPosition != expectedPosition.position) {
            throw "ESERROR_VERSION_VERSIONNOTMATCH [nextVersion="+nextPosition+"]";
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
                "causationId": d.causationId,
                "streamId": streamId,
                "position": nextPosition,
                "name": d.name,
                "data": d.data,
                "metadata": d.metadata,
                "createdUtc": created
            }

            resp.push({ position: nextPosition, created: created });
            var acceptedDoc = container.createDocument(container.getSelfLink(), doc, checkErrorFn);
            if (!acceptedDoc) {
                throw "Failed to append event on position " + nextPosition + " - Rollback. Please try to increase RU for events container.";
            }
        }

        metadata.lastPosition = nextPosition;
        metadata.lastUpdatedUtc = created;
        var acceptedMeta = container.replaceDocument(metadata._self, metadata, checkErrorFn);
        if (!acceptedMeta) {
            throw "Failed to update metadata for stream - Rollback";
        }
        response.setBody(resp);
    }

    // main function
    function run(err, metadataResults) {
        checkError(err);
        if (metadataResults.length == 0) {
            var newMeta = {
                streamId: streamId,
                type: streamType,
                lastPosition: 0
            }
            return container.createDocument(container.getSelfLink(), newMeta, createDocument);
        } else {
            return createDocument(err, metadataResults[0]);
        }
    }

    // metadata query
    var metadataQuery = 'SELECT * FROM %%CONTAINER_NAME%% e WHERE e.streamId = "' + streamId + '" AND e.type = "' + streamType + '"';
    var transactionAccepted = container.queryDocuments(container.getSelfLink(), metadataQuery, run);
    if (!transactionAccepted) throw "Transaction not accepted, rollback";
}