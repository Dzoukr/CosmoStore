module CosmoStore.Validation

let validateVersion streamId (nextVer:int64) = function
    | ExpectedVersion.Any -> ()
    | ExpectedVersion.NoStream ->
        if nextVer > 1L then
            failwithf "ESERROR_VERSION_STREAMEXISTS: Stream '%s' was expected to be empty, but contains %i events" streamId (nextVer - 1L)
    | ExpectedVersion.Exact expectedVer ->
        if nextVer <> expectedVer then
            failwithf "ESERROR_VERSION_VERSIONNOTMATCH: Stream '%s' was expected to have next version %i, but has %i" streamId expectedVer nextVer
