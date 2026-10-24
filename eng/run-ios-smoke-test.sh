#!/usr/bin/env bash
# Installs Sample.MonoGame.iOS on an iOS Simulator, runs its smoke test, and exits 0 only if the app
# logs "Exit code 0" (the pixel check plus the SkiaDirectCanvas checks). A crash or a timeout fails.
# Usage: eng/run-ios-smoke-test.sh <.app directory> <bundle id> [timeout seconds]
# IOS_SIMULATOR picks the simulator (a UDID); the default is the booted one.
set -euo pipefail

app=$1
bundle=$2
timeout=${3:-180}
device=${IOS_SIMULATOR:-booted}

xcrun simctl install "$device" "$app"
container=$(xcrun simctl get_app_container "$device" "$bundle" data)
log="$container/Documents/smoke-test.log"
rm -f "$log"

# Prints "<bundle id>: <pid>". The app is a process on this Mac, so kill -0 can watch it.
launched=$(xcrun simctl launch --terminate-running-process "$device" "$bundle" --smoke-test)
pid=${launched##*: }
echo "Launched $bundle, pid $pid"

deadline=$((SECONDS + timeout))
result=""
while (( SECONDS < deadline )); do
  result=$(grep -o "Exit code [0-9-]*" "$log" 2>/dev/null || true)
  [[ -n $result ]] && break
  if ! kill -0 "$pid" 2>/dev/null; then
    sleep 1
    result=$(grep -o "Exit code [0-9-]*" "$log" 2>/dev/null || true)
    [[ -n $result ]] && break
    cat "$log" 2>/dev/null || echo "No smoke-test log."
    echo "The app exited without finishing the smoke test (crashed)."
    xcrun simctl spawn "$device" log show --last 3m --style compact \
      --predicate "process == \"$(basename "$app" .app)\"" | tail -n 200 || true
    exit 1
  fi
  sleep 2
done

cat "$log" 2>/dev/null || echo "No smoke-test log."
if [[ -z $result ]]; then
  echo "Timed out after ${timeout}s waiting for the smoke test to finish."
  xcrun simctl terminate "$device" "$bundle" || true
  exit 1
fi
[[ $result == "Exit code 0" ]]
