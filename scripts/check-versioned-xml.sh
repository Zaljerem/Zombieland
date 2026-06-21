#!/usr/bin/env bash
set -euo pipefail

python3 - <<'PY'
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

repo = Path(".")
load_folders = ET.parse(repo / "LoadFolders.xml").getroot()
runtime_roots = (
    "Assemblies",
    "Defs",
    "Languages",
    "Libraries",
    "Patches",
    "Resources",
    "Sounds",
    "Textures",
)
xml_roots = ("Defs", "Patches", "Languages")

errors = []
checked_versions = []

for folder in runtime_roots:
    if (repo / folder).exists():
        errors.append(
            f"{folder}: root runtime folder is forbidden; put active content under a version folder"
        )

for node in list(load_folders):
    tag = node.tag
    if tag.startswith("v"):
        version = tag[1:]
    elif tag == "default":
        continue
    else:
        version = tag

    entries = [
        (child.text or "").strip()
        for child in list(node)
        if child.tag == "li"
    ]
    version_dir = repo / version

    if not version_dir.exists():
        errors.append(f"{version}: LoadFolders entry exists but {version}/ is missing")
        continue

    checked_versions.append(version)

    if entries != [version]:
        errors.append(
            f"{version}: LoadFolders must be isolated and list only {version}; got {entries}"
        )

    for folder in xml_roots:
        for xml_path in (version_dir / folder).rglob("*.xml"):
            try:
                ET.parse(xml_path)
            except ET.ParseError as exc:
                errors.append(f"{xml_path}: XML parse error: {exc}")

if not checked_versions:
    errors.append("No version folders checked from LoadFolders.xml")

if errors:
    print("Versioned XML layout check failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    sys.exit(1)

print(
    "Versioned runtime layout OK: "
    + ", ".join(checked_versions)
    + " are isolated and no root runtime folders exist"
)
PY
