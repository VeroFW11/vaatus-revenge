#!/usr/bin/env bash
# Runs the core EditMode tests with plain NUnit (no Unity needed). Extra args go to `dotnet test`,
# e.g. tools/run-core-tests.sh --filter "FullyQualifiedName~Dodge"
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
lock="${TMPDIR:-/tmp}/vaatus-revenge-build.lock"
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
exec flock -o "$lock" dotnet test "$here/CoreTests/CoreTests.csproj" -nologo -v q -nodeReuse:false "$@"
