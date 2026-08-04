#!/bin/sh
# Fails if a banned product name appears in tracked files or in this branch's commit
# messages. A linter cannot see comments or commit messages, so this runs in CI
# alongside the formatter.
#
#   ./scripts/check-terms.sh              check against the default branch
#   BASE=origin/main ./scripts/check-terms.sh
set -eu

root=$(git rev-parse --show-toplevel)
cd "$root"

terms=$(grep -v '^[[:space:]]*#' .banned-terms | grep -v '^[[:space:]]*$' || true)
if [ -z "$terms" ]; then
  echo "No banned terms configured."
  exit 0
fi

pattern=$(printf '%s' "$terms" | paste -sd'|' -)
status=0

# Tracked files only: node_modules, build output and this script's own config are
# either untracked or excluded below.
files=$(git ls-files | grep -vE '^(\.banned-terms|scripts/check-terms\.sh)$' || true)
if [ -n "$files" ]; then
  # -I skips binary files so a stray match inside an image cannot fail the build.
  if hits=$(printf '%s\n' "$files" | xargs grep -IEnil "$pattern" 2>/dev/null); then
    echo "Banned term found in tracked files:"
    printf '%s\n' "$hits" | sed 's/^/  /'
    status=1
  fi
fi

base=${BASE:-}
if [ -z "$base" ]; then
  for candidate in origin/main main origin/master master; do
    if git rev-parse --verify --quiet "$candidate" >/dev/null; then
      base=$candidate
      break
    fi
  done
fi

if [ -n "$base" ]; then
  if messages=$(git log --format='%H %s%n%b' "$base"..HEAD 2>/dev/null) && [ -n "$messages" ]; then
    if printf '%s\n' "$messages" | grep -Eiq "$pattern"; then
      echo "Banned term found in a commit message on this branch:"
      printf '%s\n' "$messages" | grep -Ein "$pattern" | sed 's/^/  /'
      status=1
    fi
  fi
fi

if [ "$status" -eq 0 ]; then
  echo "No banned terms found."
fi
exit "$status"
