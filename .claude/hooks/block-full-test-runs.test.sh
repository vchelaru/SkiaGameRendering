#!/bin/bash
# Tests for block-full-test-runs.sh. Pipes hook payloads into the hook and checks the exit code
# (0 = allowed, 2 = blocked). Run: bash .claude/hooks/block-full-test-runs.test.sh
# Payloads are JSON as Claude Code sends them, so \n and \" are JSON escapes.
hook="$(dirname "$0")/block-full-test-runs.sh"
failures=0

check() {
  expected=$1
  name=$2
  command_json=$3
  tool=${4:-Bash}
  printf '{"session_id":"s","hook_event_name":"PreToolUse","tool_name":"%s","tool_input":{"command":"%s","description":"d"}}' "$tool" "$command_json" \
    | bash "$hook" 2>/dev/null
  actual=$?
  if [ "$actual" -eq "$expected" ]; then
    echo "PASS  $name"
  else
    echo "FAIL  $name (expected $expected, got $actual)"
    failures=$((failures + 1))
  fi
}

allowed=0
blocked=2
p='tests/Tests.Core/Tests.Core.csproj'

check $blocked "plain unfiltered run" "dotnet test $p"
check $allowed "--filter" "dotnet test $p --filter \\\"SkiaRendererTests\\\""
check $allowed "--filter=value" "dotnet test $p --filter=SkiaRendererTests"
check $allowed "dotnet.exe" "dotnet.exe test $p --filter SkiaRendererTests"
check $blocked "Tests.proj" 'dotnet test tests/Tests.proj'
check $blocked "Tests.proj with a class filter" 'dotnet test tests/Tests.proj --filter \"SkiaRendererTests\"'
check $blocked "solution with a class filter" 'dotnet test SkiaGameRendering.sln --filter \"SkiaRendererTests\"'
check $blocked "no path with a class filter" 'dotnet test --filter \"SkiaRendererTests\"'
check $allowed "quoted project path" "dotnet test \\\"$p\\\" --filter SkiaRendererTests"
check $allowed "dotnet build" 'dotnet build SkiaGameRendering.sln'
check $allowed "SGR_ALLOW_FULL_TESTS=1 prefix" 'SGR_ALLOW_FULL_TESTS=1 dotnet test tests/Tests.proj'
check $allowed "PowerShell \$env override" '$env:SGR_ALLOW_FULL_TESTS=1; dotnet test tests/Tests.proj' PowerShell
check $allowed "PowerShell \$env override, quoted" "\$env:SGR_ALLOW_FULL_TESTS = '1'; dotnet test tests/Tests.proj" PowerShell
check $blocked "PowerShell \$env override set to 0" '$env:SGR_ALLOW_FULL_TESTS=0; dotnet test tests/Tests.proj' PowerShell
check $blocked "PowerShell call operator" "& dotnet test $p" PowerShell
check $blocked "other env prefix" "FOO=1 dotnet test $p"
check $blocked "after cd &&" "cd x && dotnet test $p"
check $blocked "after ;" 'echo hi; dotnet test'
check $blocked "after ||" 'false || dotnet test'
check $blocked "after newline" 'echo hi\ndotnet test'
check $blocked "allow flag on a different command" 'SGR_ALLOW_FULL_TESTS=1 echo hi && dotnet test'
check $blocked "filter on a different command" "dotnet test $p && dotnet test $p --filter Y"
check $allowed "double-quoted string" 'git commit -m \"blocks dotnet test without filter\"'
check $allowed "single-quoted string" "echo 'run dotnet test here'"
check $allowed "multi-line quoted string" 'git commit -m \"line one\ndotnet test\nline three\"'
check $allowed "heredoc body" 'cat > f.md <<'"'"'EOF'"'"'\nUse dotnet test with a filter.\ndotnet test\nEOF'
check $blocked "command after heredoc" 'cat > f.md <<EOF\nnotes\nEOF\ndotnet test'
check $allowed "dotnet test-like word" 'dotnet testx'
check $blocked "namespace filter" "dotnet test $p --filter \\\"Tests.CoreAngle\\\""
check $blocked "namespace filter with property prefix" "dotnet test $p --filter \\\"FullyQualifiedName~Tests.Godot\\\""
check $blocked "namespace filter, unquoted --filter=" "dotnet test $p --filter=Tests.CoreAngle"
check $blocked "namespace filter, single-quoted" "dotnet test $p --filter 'Tests.CoreAngle'"
check $blocked "one class and one namespace" "dotnet test $p --filter \\\"SkiaRendererTests|Tests.Shared\\\""
check $allowed "namespace filter with SGR_ALLOW_FULL_TESTS=1" "SGR_ALLOW_FULL_TESTS=1 dotnet test $p --filter \\\"Tests.CoreAngle\\\""
check $allowed "qualified class" "dotnet test $p --filter \\\"FullyQualifiedName~Tests.CoreAngle.AngleSkiaPixelReadbackTests\\\""
check $allowed "qualified method" "dotnet test $p --filter \\\"FullyQualifiedName~Tests.Core.SkiaRendererTests.Draw_Clears\\\""
check $allowed "bare classes" "dotnet test $p --filter \\\"SkiaRendererTests|SkiaDirectCanvasTests\\\""
check $allowed "method name filter" "dotnet test $p --filter \\\"Name~Draw_Clears\\\""
check $blocked "bare word" "dotnet test $p --filter \\\"Golden\\\""
check $blocked "bare word with property prefix" "dotnet test $p --filter \\\"FullyQualifiedName~Leak\\\""
check $allowed "trait filter" "dotnet test $p --filter \\\"Category=Slow\\\""
check $allowed "class in parentheses" "dotnet test $p --filter \\\"(FullyQualifiedName~Ns.SkiaRendererTests.|FullyQualifiedName~Ns.SkiaRendererTests+)\\\""

if [ "$failures" -ne 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
echo "All passed"
