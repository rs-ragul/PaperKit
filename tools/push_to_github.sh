#!/usr/bin/env bash
# Put PaperKit on GitHub - ONE repository, both apps.
#
#   bash tools/push_to_github.sh YOUR_GITHUB_USERNAME
#
# The whole project folder is pushed (Windows app + Android app in one repo).
# GitHub Actions then builds:
#     PaperKit-windows-x64.zip   (the native Windows app)
#     PaperKit-android.apk       (the native Android app)
# and attaches both to the repo's Releases page.
#
# Needs: git, and the GitHub CLI (gh) for creating the repo automatically.
# If gh is missing, create the empty repo by hand at
# https://github.com/new (name it PaperKit, tick "public") and run this again.
set -euo pipefail
cd "$(dirname "$0")/.."

USER="${1:-}"
REPO="${2:-PaperKit}"
VERSION="1.0.0"

if [ -z "$USER" ]; then
  echo "usage: bash tools/push_to_github.sh YOUR_GITHUB_USERNAME [repo-name]"
  echo "example: bash tools/push_to_github.sh johndoe"
  exit 1
fi

if ! command -v git >/dev/null 2>&1; then
  echo "ERROR: git is not installed. Install Git for Windows: https://git-scm.com/download/win"
  exit 1
fi

REMOTE="https://github.com/$USER/$REPO.git"

echo ""
echo "  PaperKit  ->  https://github.com/$USER/$REPO"
echo ""
echo "==> 1/4  checking that everything still works"
if command -v dotnet >/dev/null 2>&1; then
  dotnet test native/PaperKit.Spec/PaperKit.Spec.csproj -c Release | tail -3
else
  echo "    (dotnet is not installed - the tests are skipped here,"
  echo "     GitHub Actions runs them on every push)"
fi

echo "==> 2/4  committing the project"
git init -b main >/dev/null 2>&1 || git init >/dev/null
git add -A
git -c user.name="PaperKit" -c user.email="$USER@users.noreply.github.com" \
  commit -q -m "PaperKit $VERSION - the offline PDF toolbox for Windows and Android" || true

echo "==> 3/4  creating the GitHub repository"
git remote remove origin >/dev/null 2>&1 || true
git remote add origin "$REMOTE"
if command -v gh >/dev/null 2>&1; then
  gh repo create "$REPO" --public --source=. --remote=origin --description \
    "The offline PDF toolbox for Windows and Android - merge, split, organise, convert, secure and verify. 100% offline, your files never leave the device." \
    || echo "    (the repo already exists - continuing)"
  gh repo edit "$REPO" --add-topic pdf --add-topic pdf-tools --add-topic offline \
    --add-topic privacy --add-topic avalonia --add-topic dotnet --add-topic csharp \
    --add-topic digital-signature --add-topic pades --add-topic cca-india || true
else
  echo "    gh (GitHub CLI) is not installed."
  echo "    -> create the repository by hand:  https://github.com/new"
  echo "       name it '$REPO', make it public, then run this script again."
  exit 1
fi

echo "==> 4/4  pushing"
git push -u origin main
git tag -a "v$VERSION" -m "PaperKit $VERSION"
git push origin "v$VERSION"

cat <<'TXT'

  DONE.

  1. Open  https://github.com/YOURNAME/PaperKit/actions
     GitHub is building the Windows app and the Android app now (3-6 minutes).

  2. When the green tick appears, open  .../releases/tag/v1.0.0
     and download:
        PaperKit-windows-x64.zip        <- extract it, run PaperKit.exe
        PaperKit-android.apk            <- copy this to your phone

TXT
