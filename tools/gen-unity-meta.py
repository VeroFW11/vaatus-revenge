#!/usr/bin/env python3
"""Creates missing Unity .meta files for new files and folders under game/Assets/_Project.

Unity normally writes .meta files itself when it imports a file. When code is written without the
editor open (e.g. by a cloud Claude session), committing the .meta files keeps every machine on
the same asset GUIDs, so references between assets don't break when David and Jeremy both import.

Usage: python3 tools/gen-unity-meta.py [--dry-run] [paths...]
  With paths, only those files/folders (and their parent folders) get .meta files.
"""
import pathlib
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parent.parent / "game" / "Assets" / "_Project"

TEMPLATES = {
    "folder": "fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    ".cs": "fileFormatVersion: 2\nguid: {guid}\n",
    ".asmdef": "fileFormatVersion: 2\nguid: {guid}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    ".md": "fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    ".txt": "fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    ".json": "fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
}


def main():
    dry = "--dry-run" in sys.argv
    only = [pathlib.Path(a).resolve() for a in sys.argv[1:] if not a.startswith("--")]
    created, skipped = [], []
    paths = [ROOT] + sorted(ROOT.rglob("*"))
    if only:
        wanted = set()
        for target in only:
            wanted.add(target)
            wanted.update(p for p in target.parents if ROOT == p or ROOT in p.parents)
        paths = [p for p in paths if p.resolve() in wanted]
    for path in paths:
        if path.name.startswith(".") or path.suffix == ".meta" or any(p.startswith(".") for p in path.relative_to(ROOT.parent).parts):
            continue
        meta = path.with_name(path.name + ".meta")
        if meta.exists():
            continue
        kind = "folder" if path.is_dir() else path.suffix.lower()
        template = TEMPLATES.get(kind)
        if template is None:
            skipped.append(path)
            continue
        if not dry:
            meta.write_text(template.format(guid=uuid.uuid4().hex), encoding="utf-8", newline="\n")
        created.append(meta)

    for m in created:
        print(("would create " if dry else "created ") + str(m.relative_to(ROOT.parent.parent)))
    for s in skipped:
        print("no template (let Unity create it): " + str(s.relative_to(ROOT.parent.parent)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
