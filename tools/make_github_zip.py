#!/usr/bin/env python3
"""Build the single zip that goes to GitHub.

    deliverables/PaperKit-github.zip

It contains the whole project (Windows app + Android app + the native
rewrite in progress), without build artefacts or the git history. Extract it
into your PaperKit repository folder, commit and push.
"""
import os
import shutil
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "deliverables" / "PaperKit-github.zip"

SKIP_DIRS = {
    "node_modules", ".git", ".gradle", "__pycache__", ".idea", ".vscode",
    ".pytest_cache", "release", "bin", "obj", ".vs", "deliverables",
    ".avalonia-build-tasks", "avalonia-build-tasks", "TestResults",
}
SKIP_FILES = {".DS_Store", "*.apk", "*.exe", "*.aab", "*.log"}
SKIP_SUFFIX = {".apk", ".exe", ".aab", ".log", ".pyc"}


def skip(rel: Path) -> bool:
    parts = rel.parts
    if any(p in SKIP_DIRS for p in parts):
        return True
    # the generated web bundle and the packaged source trees are rebuilt
    if "dist" in parts and parts[0] in ("app", "shells"):
        return True
    if parts and parts[0] == "dist":
        return True
    return rel.suffix.lower() in SKIP_SUFFIX


def main() -> int:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    if OUT.exists():
        OUT.unlink()

    count = 0
    tmp = OUT.with_suffix(".zip.tmp")
    if tmp.exists():
        tmp.unlink()
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for dirpath, dirnames, filenames in os.walk(ROOT):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for fn in filenames:
                path = Path(dirpath) / fn
                rel = path.relative_to(ROOT)
                if skip(rel):
                    continue
                # the files land directly in the repository root
                z.write(path, arcname=str(rel))
                count += 1
    tmp.replace(OUT)

    print(f"  {count} files -> {OUT}")
    print(f"  {OUT.stat().st_size / 1024 / 1024:.2f} MB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
