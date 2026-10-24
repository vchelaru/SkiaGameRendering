#!/usr/bin/env bash
# Installs an Android sample (Sample.Kni.Android or Sample.MonoGame.Android) on the connected device or
# emulator, runs its smoke test, and exits 0 only if the activity logs "Exit code 0" (the pixel check
# plus the SkiaDirectCanvas checks).
# Usage: eng/run-android-smoke-test.sh <apk> <application id> [timeout seconds]
set -euo pipefail

apk=$1
package=$2
timeout=${3:-180}

adb install -r "$apk"
# The activity's Java name carries a crc64 hash, so ask the package manager for it.
component=$(adb shell cmd package resolve-activity --brief "$package" | tail -n 1 | tr -d '\r')
echo "Launching $component"

adb logcat -c
adb shell am start -n "$component" --ez smoke true

deadline=$((SECONDS + timeout))
result=""
while (( SECONDS < deadline )); do
  result=$(adb logcat -d -s SkiaSample | grep -o "Exit code [0-9-]*" || true)
  [[ -n $result ]] && break
  if adb logcat -d -b crash | grep -q "Process: $package"; then
    adb logcat -d -b crash
    echo "The app crashed."
    exit 1
  fi
  sleep 2
done

adb logcat -d -s SkiaSample DOTNET AndroidRuntime monodroid
if [[ -z $result ]]; then
  echo "Timed out after ${timeout}s waiting for the smoke test to finish."
  adb logcat -d | tail -n 200
  exit 1
fi
[[ $result == "Exit code 0" ]]
