#!/usr/bin/env python3
"""Startup dependency checker for npm and pip projects.

The script checks dependencies listed in package.json and requirements.txt.
If any dependencies are missing, it runs:
  - npm install
  - pip install -r requirements.txt
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PACKAGE_JSON = ROOT / "package.json"
REQUIREMENTS_TXT = ROOT / "requirements.txt"


class DependencyCheckError(RuntimeError):
    """Raised when dependency checks cannot continue."""


def run_command(command: list[str], description: str) -> subprocess.CompletedProcess[str]:
    """Run a command and return its completed process output."""
    try:
        return subprocess.run(
            command,
            cwd=ROOT,
            check=False,
            capture_output=True,
            text=True,
        )
    except OSError as exc:
        raise DependencyCheckError(f"Failed to execute {description}: {exc}") from exc


def parse_requirements(requirements_file: Path) -> list[str]:
    """Extract package names from requirements.txt lines."""
    packages: list[str] = []
    for raw_line in requirements_file.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue

        line = line.split("#", 1)[0].strip()
        if not line:
            continue

        if line.startswith(("-r", "--requirement", "-e", "--editable", "git+", "http://", "https://")):
            continue

        match = re.match(r"^([A-Za-z0-9_.-]+)", line)
        if match:
            packages.append(match.group(1))

    return packages


def check_npm_dependencies() -> bool:
    """Return True when npm dependencies are present or installed successfully."""
    if not PACKAGE_JSON.exists():
        print("[npm] package.json not found. Skipping npm dependency check.")
        return True

    if shutil.which("npm") is None:
        raise DependencyCheckError("[npm] npm is not installed or not available in PATH.")

    try:
        package_data = json.loads(PACKAGE_JSON.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise DependencyCheckError(f"[npm] package.json is invalid JSON: {exc}") from exc

    required = set(package_data.get("dependencies", {}).keys())
    required.update(package_data.get("devDependencies", {}).keys())

    if not required:
        print("[npm] No dependencies listed in package.json.")
        return True

    print("[npm] Checking installed Node dependencies...")
    ls_result = run_command(["npm", "ls", "--depth=0", "--json"], "npm ls")

    installed: set[str] = set()
    if ls_result.stdout:
        try:
            ls_data = json.loads(ls_result.stdout)
            installed = set((ls_data.get("dependencies") or {}).keys())
        except json.JSONDecodeError:
            installed = set()

    missing = sorted(required - installed)
    if missing:
        print(f"[npm] Missing dependencies detected: {', '.join(missing)}")
        print("[npm] Running npm install...")
        install_result = run_command(["npm", "install"], "npm install")
        if install_result.returncode != 0:
            print(install_result.stdout)
            print(install_result.stderr, file=sys.stderr)
            raise DependencyCheckError("[npm] npm install failed.")
        print("[npm] npm dependencies installed successfully.")
    else:
        print("[npm] All npm dependencies are present.")

    return True


def check_pip_dependencies() -> bool:
    """Return True when pip dependencies are present or installed successfully."""
    if not REQUIREMENTS_TXT.exists():
        print("[pip] requirements.txt not found. Skipping pip dependency check.")
        return True

    pip_version = run_command([sys.executable, "-m", "pip", "--version"], "python -m pip --version")
    if pip_version.returncode != 0:
        print(pip_version.stdout)
        print(pip_version.stderr, file=sys.stderr)
        raise DependencyCheckError("[pip] pip is not available for this Python interpreter.")

    required = parse_requirements(REQUIREMENTS_TXT)
    if not required:
        print("[pip] No installable dependencies found in requirements.txt.")
        return True

    print("[pip] Checking installed Python dependencies...")
    missing: list[str] = []

    for package in required:
        show_result = run_command([sys.executable, "-m", "pip", "show", package], f"pip show {package}")
        if show_result.returncode != 0:
            missing.append(package)

    if missing:
        print(f"[pip] Missing dependencies detected: {', '.join(sorted(set(missing)))}")
        print("[pip] Running pip install -r requirements.txt...")
        install_result = run_command(
            [sys.executable, "-m", "pip", "install", "-r", str(REQUIREMENTS_TXT)],
            "pip install -r requirements.txt",
        )
        if install_result.returncode != 0:
            print(install_result.stdout)
            print(install_result.stderr, file=sys.stderr)
            raise DependencyCheckError("[pip] pip install -r requirements.txt failed.")
        print("[pip] Python dependencies installed successfully.")
    else:
        print("[pip] All Python dependencies are present.")

    return True


def main() -> int:
    """Run dependency checks and installs for npm and pip."""
    try:
        check_npm_dependencies()
        check_pip_dependencies()
    except DependencyCheckError as exc:
        print(f"Dependency check failed: {exc}", file=sys.stderr)
        return 1

    print("All dependencies are present.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
