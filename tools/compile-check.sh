#!/usr/bin/env bash
# Compiles the game's C# outside Unity, the way Unity would, and runs the project lint.
#   tools/compile-check.sh          -> exit 0 when everything compiles and lint passes
# Assemblies checked (each mirrors an .asmdef in game/Assets/_Project):
#   VaatusRevenge.Core    pure C#, no UnityEngine allowed
#   VaatusRevenge.Game    runtime scripts, checked twice: in-editor (UNITY_EDITOR) and player build
#   VaatusRevenge.Editor  editor tools
# Caveat: Unity APIs come from Unity 2021.3 reference DLLs and a stub of the Input System, not the
# real 6000.6 editor, so a clean result here is strong evidence, not proof. Unity has the last word.
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
lock="${TMPDIR:-/tmp}/vaatus-revenge-build.lock"

# Several agents may run this at once, so builds take turns via a lock. flock -o keeps the lock out
# of child processes, and node reuse is off so no MSBuild worker lingers after the build.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [ "${1:-}" != "--locked" ]; then
  exec flock -o "$lock" "$0" --locked
fi

status=0

run() {
  local label="$1"; shift
  local log; log="$(mktemp)"
  if dotnet build "$@" -nologo -v q -clp:NoSummary -nodeReuse:false > "$log" 2>&1; then
    local warns; warns=$(grep -E ": warning CS" "$log" | grep -v "/InputSystemStub/" | sort -u)
    echo "PASS  $label"
    if [ -n "$warns" ]; then echo "$warns" | sed 's#^.*/game/#  game/#' | head -40; fi
  else
    echo "FAIL  $label"
    grep -E ": (error|warning) [A-Z]+[0-9]+" "$log" | grep -v "/InputSystemStub/.*warning" | sort -u | sed 's#^.*/game/#  game/#' | head -80
    grep -qE ": error" "$log" || tail -20 "$log"
    status=1
  fi
  rm -f "$log"
}

run "VaatusRevenge.Core (pure C#)"        "$here/UnityCompileCheck/Core.Check.csproj"
run "VaatusRevenge.Game (in editor)"      "$here/UnityCompileCheck/Game.Check.csproj"
run "VaatusRevenge.Game (player build)"   "$here/UnityCompileCheck/Game.Check.csproj" -p:UnityEditorDefine=false
run "VaatusRevenge.Editor"                "$here/UnityCompileCheck/Editor.Check.csproj"

if python3 "$here/unity-lint.py"; then echo "PASS  project lint"; else echo "FAIL  project lint"; status=1; fi
exit $status
