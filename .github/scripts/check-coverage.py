#!/usr/bin/env python3
"""Fail a build whose merged coverage falls below the agreed floor.

The thresholds are set just under the measured baseline rather than at a round
number, so a real regression is caught while ordinary movement between commits
is not. Raise them when the baseline rises; they are a ratchet, not a target.

Usage: check-coverage.py COBERTURA_XML [--min-line PCT] [--min-branch PCT]
"""

from __future__ import annotations

import argparse
import os
import sys
import xml.etree.ElementTree as ElementTree


def write_job_summary(lines):
    """Append markdown to the GitHub job summary when running in Actions."""
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary_path:
        return

    with open(summary_path, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")


def main(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("report")
    parser.add_argument("--min-line", type=float, default=89.0)
    parser.add_argument("--min-branch", type=float, default=75.0)
    arguments = parser.parse_args(argv[1:])

    if not os.path.exists(arguments.report):
        print(f"::error::No coverage report at {arguments.report}.")
        return 1

    root = ElementTree.parse(arguments.report).getroot()
    line_rate = float(root.get("line-rate", 0)) * 100
    branch_rate = float(root.get("branch-rate", 0)) * 100
    lines_valid = root.get("lines-valid", "?")
    branches_valid = root.get("branches-valid", "?")

    line_ok = line_rate >= arguments.min_line
    branch_ok = branch_rate >= arguments.min_branch

    print(f"Line coverage:   {line_rate:.2f}% of {lines_valid} lines "
          f"(minimum {arguments.min_line}%) {'OK' if line_ok else 'BELOW'}")
    print(f"Branch coverage: {branch_rate:.2f}% of {branches_valid} branches "
          f"(minimum {arguments.min_branch}%) {'OK' if branch_ok else 'BELOW'}")

    write_job_summary([
        "### Coverage thresholds",
        "",
        "| Metric | Measured | Minimum | Result |",
        "| --- | ---: | ---: | --- |",
        f"| Line | {line_rate:.2f}% | {arguments.min_line}% | "
        f"{'pass' if line_ok else 'fail'} |",
        f"| Branch | {branch_rate:.2f}% | {arguments.min_branch}% | "
        f"{'pass' if branch_ok else 'fail'} |",
    ])

    if not line_ok:
        print(f"::error::Line coverage {line_rate:.2f}% is below the "
              f"{arguments.min_line}% floor.")
    if not branch_ok:
        print(f"::error::Branch coverage {branch_rate:.2f}% is below the "
              f"{arguments.min_branch}% floor.")

    return 0 if line_ok and branch_ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
