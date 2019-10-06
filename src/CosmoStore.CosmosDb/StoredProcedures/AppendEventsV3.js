function storedProcedure(streamId, documentsToCreate, expectedVersion) {

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

    function checkVersion(nextVersion) {
        if (expectedVersion.mode == "any") {
            return;
        }
        if (expectedVersion.mode == "noStream" && nextVersion > 1) {
            throw "ESERROR_VERSION_STREAMEXISTS";
        }
        if (expectedVersion.mode == "exact" && nextVersion != expectedVersion.version) {
            throw "ESERROR_VERSION_VERSIONNOTMATCH";
        }
    }

    // append event
    function createDocument(err, metadata) {
        if (err) throw new Error("Error" + err.message);
        checkVersion(metadata.lastVersion + 1);
        var nextVersion = metadata.lastVersion;
        var resp = [];
        for (var i in documentsToCreate) {
            nextVersion++;
            var d = documentsToCreate[i];
            var created = new Date().toISOString();
            var doc = {
                "type": eventType,
                "id": d.id,
                "correlationId": d.correlationId,
                "causationId": d.causationId,
                "streamId": streamId,
                "version": nextVersion,
                "name": d.name,
                "data": d.data,
                "metadata": d.metadata,
                "createdUtc": created
            }

            resp.push({ version: nextVersion, created: created });
            var acceptedDoc = container.createDocument(container.getSelfLink(), doc, checkErrorFn);
            if (!acceptedDoc) {
                throw "Failed to append event on version " + nextVersion + " - Rollback. Please try to increase RU for events container.";
            }
        }

        metadata.lastVersion = nextVersion;
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
                lastVersion: 0
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