#!/usr/bin/env python3
"""Fail a build whose test run skipped tests instead of running them.

A suite that skips itself when a dependency is missing prints the same
"Failed: 0" as a suite that passed, which is indistinguishable from success at
exactly the moment the difference matters. This reads the TRX results and
treats a skip as a failure.

The per-run <Counters> element is deliberately not trusted: it reports
notExecuted="0" for a run in which every gated test was skipped, so the
individual <UnitTestResult> outcomes are counted instead.

Usage: check-test-results.py [RESULTS_DIR]
"""

from __future__ import annotations

import glob
import os
import sys
import xml.etree.ElementTree as ElementTree

NAMESPACE = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

# MSTest reports both Assert.Inconclusive and [Ignore] as NotExecuted.
SKIPPED_OUTCOMES = frozenset({"NotExecuted", "Inconclusive", "Pending"})


def summarize(trx_path):
    """Return (passed, failed, skipped, skipped_names) for one TRX file."""
    root = ElementTree.parse(trx_path).getroot()

    passed = 0
    failed = 0
    skipped_names = []

    for result in root.findall(".//t:UnitTestResult", NAMESPACE):
        outcome = result.get("outcome") or "Unknown"
        if outcome == "Passed":
            passed += 1
        elif outcome in SKIPPED_OUTCOMES:
            skipped_names.append(result.get("testName") or "<unnamed>")
        else:
            failed += 1

    return passed, failed, skipped_names


def main(argv):
    results_dir = argv[1] if len(argv) > 1 else "artifacts/test-results"
    trx_files = sorted(
        glob.glob(os.path.join(results_dir, "**", "*.trx"), recursive=True))

    if not trx_files:
        print(f"::error::No .trx results found under {results_dir}. "
              "The test step did not produce results.")
        return 1

    total_passed = 0
    total_failed = 0
    total_skipped = []

    for trx_path in trx_files:
        passed, failed, skipped_names = summarize(trx_path)
        total_passed += passed
        total_failed += failed
        total_skipped.extend(skipped_names)

        print(f"{os.path.basename(trx_path)}: "
              f"{passed} passed, {failed} failed, {len(skipped_names)} skipped")

    print(f"\nTotal: {total_passed} passed, {total_failed} failed, "
          f"{len(total_skipped)} skipped, across {len(trx_files)} result file(s)")

    if total_passed + total_failed + len(total_skipped) == 0:
        print("::error::The run contained no tests at all.")
        return 1

    if total_skipped:
        print(f"::error::{len(total_skipped)} test(s) were skipped rather than "
              "run. A skipped suite reports the same success as a passing one, "
              "so CI treats it as a failure. Check that Docker is available and "
              "that RUN_MONGODB_INTEGRATION_TESTS=true is set.")
        for name in sorted(total_skipped):
            print(f"  skipped: {name}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
