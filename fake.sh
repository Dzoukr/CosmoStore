#!/usr/bin/env bash

set -eu
set -o pipefail

dotnet restore build.proj
dotnet fake run build.fsx --target "$@"