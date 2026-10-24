#!/bin/bash
# PreToolUse(Bash|PowerShell): refuses a `dotnet test` that does not name one test .csproj
# (tests/Tests.proj and SkiaGameRendering.sln build every backend), has no --filter, or has a
# --filter naming a namespace or a bare word. CI runs the full matrix; local runs are the test
# classes under work (CLAUDE.md "Building and Testing"). For the rare case the user asks for a
# full run, prefix the command with SGR_ALLOW_FULL_TESTS=1 (bash) or set
# $env:SGR_ALLOW_FULL_TESTS=1 earlier in the same command (PowerShell).
#
# It only sees `dotnet test`. Smoke-test exes, Playwright, Godot and Unity runs are rules in
# CLAUDE.md that nothing enforces.
#
# Only a `dotnet test` that runs as a command counts: at the start of the command, after
# ; & | or a newline, or after NAME=value prefixes. Text inside quoted strings and heredoc
# bodies is ignored. Tests: block-full-test-runs.test.sh. Parses the hook JSON with awk so
# it needs no jq (portable to macOS bash 3.2 / BSD awk and Git Bash on Windows).
input=$(cat)

# Fast path: most commands never mention dotnet.
case "$input" in
  *dotnet*) ;;
  *) exit 0 ;;
esac

printf '%s' "$input" | awk -v sq="'" '
{ buf = buf (NR > 1 ? "\n" : "") $0 }

END {
  # Pull the tool_input.command JSON string out and decode its escapes.
  i = index(buf, "\"command\"")
  if (i == 0) exit 0
  rest = substr(buf, i + 9)
  if (!match(rest, /^[ \t\r\n]*:[ \t\r\n]*"/)) exit 0
  rest = substr(rest, RLENGTH + 1)
  n = length(rest)
  cmd = ""
  for (p = 1; p <= n; p++) {
    c = substr(rest, p, 1)
    if (c == "\"") break
    if (c == "\\") {
      p++
      e = substr(rest, p, 1)
      if (e == "n") cmd = cmd "\n"
      else if (e == "t") cmd = cmd "\t"
      else if (e == "r") cmd = cmd "\r"
      else if (e == "u") { cmd = cmd "?"; p += 4 }
      else cmd = cmd e
    } else cmd = cmd c
  }

  # PowerShell has no NAME=value prefix, so $env:SGR_ALLOW_FULL_TESTS=1 anywhere in the command allows it.
  psAllowed = (tolower(cmd) ~ ("\\$env:sgr_allow_full_tests[ \t]*=[ \t]*[\"" sq "]?1"))

  # Drop quoted strings and heredoc bodies, keep everything else. Each quoted string leaves a
  # \001N\002 token behind and its text is kept in qv[N], so a quoted --filter value can still be read.
  out = ""
  state = "normal"
  delim = ""
  nq = 0
  qbuf = ""
  n = length(cmd)
  for (p = 1; p <= n; p++) {
    c = substr(cmd, p, 1)
    if (state == "squote") {
      if (c == sq) { state = "normal"; qv[++nq] = qbuf; out = out "\001" nq "\002" }
      else qbuf = qbuf c
      continue
    }
    if (state == "dquote") {
      if (c == "\\") { p++; qbuf = qbuf substr(cmd, p, 1) }
      else if (c == "\"") { state = "normal"; qv[++nq] = qbuf; out = out "\001" nq "\002" }
      else qbuf = qbuf c
      continue
    }
    if (c == "\\") { p++; out = out "_"; continue }
    if (c == sq) { state = "squote"; qbuf = ""; continue }
    if (c == "\"") { state = "dquote"; qbuf = ""; continue }
    if (substr(cmd, p, 2) == "<<" && substr(cmd, p + 2, 1) != "<") {
      # Heredoc operator: read the delimiter word, minus any quoting.
      q = p + 2
      if (substr(cmd, q, 1) == "-") q++
      while (substr(cmd, q, 1) == " " || substr(cmd, q, 1) == "\t") q++
      d = ""
      while (q <= n) {
        dc = substr(cmd, q, 1)
        if (dc ~ /[ \t\n;&|<>()]/) break
        if (dc != sq && dc != "\"" && dc != "\\") d = d dc
        q++
      }
      if (d != "") delim = d
      p = q - 1
      out = out " "
      continue
    }
    if (c == "\n" && delim != "") {
      # Skip the heredoc body through its terminator line.
      p++
      while (p <= n) {
        e = index(substr(cmd, p), "\n")
        line = (e == 0) ? substr(cmd, p) : substr(cmd, p, e - 1)
        p = (e == 0) ? n + 1 : p + e
        sub(/^[ \t]+/, "", line)
        if (line == delim) break
      }
      p--
      delim = ""
      out = out "\n"
      continue
    }
    out = out c
  }

  # Each segment between ; & | and newlines is one command.
  segCount = split(out, segs, /[;&|\n]/)
  for (s = 1; s <= segCount; s++) {
    seg = segs[s]
    sub(/^[ \t\r({!]+/, "", seg)
    allowed = 0
    while (match(seg, /^[A-Za-z_][A-Za-z0-9_]*=[^ \t]*[ \t]+/)) {
      if (substr(seg, 1, RLENGTH) ~ /^SGR_ALLOW_FULL_TESTS=1[ \t]/) allowed = 1
      seg = substr(seg, RLENGTH + 1)
    }
    if (seg !~ /^dotnet(\.exe)?[ \t]+test([ \t)]|$)/) continue
    if (allowed || psAllowed) continue
    # Only one named test project counts. tests/Tests.proj, the .sln, or no path builds every
    # backend, even with a class filter.
    hasProject = 0
    tokCount = split(seg, toks, /[ \t]+/)
    for (k = 1; k <= tokCount; k++) {
      tok = toks[k]
      if (substr(tok, 1, 1) == "\001") tok = qv[substr(tok, 2, index(tok, "\002") - 2) + 0]
      if (tok ~ /\.csproj$/) hasProject = 1
    }
    if (!hasProject) exit 4
    if (!match(seg, /[ \t]--filter([ \t=]+|$)/)) exit 2
    # A namespace or bare-word filter (Tests.CoreAngle, Golden) runs tests that CI already runs.
    # A name term must contain a class segment (a name ending in Test or Tests, not "Tests" itself)
    # or be a method name with an underscore; Ns.Class, Ns.Class.Method, SkiaRendererTests and
    # Draw_Clears pass.
    # Filters on other properties (Category=, Trait) are not names and pass.
    rest = substr(seg, RSTART + RLENGTH)
    if (substr(rest, 1, 1) == "\001") {
      value = qv[substr(rest, 2, index(rest, "\002") - 2) + 0]
    } else {
      match(rest, /^[^ \t]*/)
      value = substr(rest, 1, RLENGTH)
    }
    termCount = split(value, terms, /[|&()]/)
    for (t = 1; t <= termCount; t++) {
      term = terms[t]
      gsub(/^[ \t]+|[ \t]+$/, "", term)
      if (match(term, /^[A-Za-z]+(~|=|!=|!~)/)) {
        prop = substr(term, 1, RLENGTH)
        term = substr(term, RLENGTH + 1)
        if (prop !~ /^(FullyQualifiedName|Name|DisplayName|ClassName)/) continue
      }
      if (term == "") continue
      if (term !~ /(^|\.)[A-Za-z0-9_]+Tests?(\.|\+|$)/ && index(term, "_") == 0) exit 3
    }
  }
  exit 0
}'

status=$?
if [ $status -eq 2 ]; then
  echo "Blocked: 'dotnet test' without --filter runs a whole test project, which CI already runs. Name classes: dotnet test tests/<Project>/<Project>.csproj --filter \"FullyQualifiedName~<ClassTests>\". If the user asked for a full run, prefix the command with SGR_ALLOW_FULL_TESTS=1 (PowerShell: \$env:SGR_ALLOW_FULL_TESTS=1; first)." >&2
  exit 2
fi
if [ $status -eq 3 ]; then
  echo "Blocked: this --filter names a namespace or a bare word, not a test class, so it runs tests that CI already runs. Name classes: --filter \"FullyQualifiedName~<ClassA>|FullyQualifiedName~<ClassB>\". If the user asked for a broad run, prefix the command with SGR_ALLOW_FULL_TESTS=1 (PowerShell: \$env:SGR_ALLOW_FULL_TESTS=1; first)." >&2
  exit 2
fi
if [ $status -eq 4 ]; then
  echo "Blocked: 'dotnet test' must name one test .csproj. tests/Tests.proj, the .sln, or no path builds every backend, which CI already does. Use dotnet test tests/<Project>/<Project>.csproj --filter \"FullyQualifiedName~<ClassTests>\". If the user asked for a full run, prefix the command with SGR_ALLOW_FULL_TESTS=1 (PowerShell: \$env:SGR_ALLOW_FULL_TESTS=1; first)." >&2
  exit 2
fi
exit 0
