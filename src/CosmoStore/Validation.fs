module CosmoStore.Validation

let validateVersion streamId (nextVer: int64) =
    function
    | ExpectedVersion.Any -> ()
    | ExpectedVersion.NoStream ->
        if nextVer > 1L then
            let nvText = sprintf "[nextVersion=%i]" nextVer
            failwithf "ESERROR_VERSION_STREAMEXISTS: %s Stream '%s' was expected to be empty, but contains %i events"
                nvText streamId (nextVer - 1L)
    | ExpectedVersion.Exact expectedVer ->
        if nextVer <> expectedVer then
            let nvText = sprintf "[nextVersion=%i]" nextVer
            failwithf
                "ESERROR_VERSION_VERSIONNOTMATCH: %s Stream '%s' was expected to have next version %i, but has %i"
                nvText streamId expectedVer nextVer
