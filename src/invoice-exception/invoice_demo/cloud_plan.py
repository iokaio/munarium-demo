# SPDX-License-Identifier: Apache-2.0
"""Distinct acceptance scenarios; each cloud provider gets at least two cases."""

CLOUD_CASES = {
    "openai": ("case-001", "case-003", "case-005"),  # Match, missing receipt, total.
    "anthropic": ("case-002", "case-004", "case-006"),  # Partial receipt, price, order.
    "openrouter": ("case-019", "case-020"),  # Independently checked duplicate invoices.
}


def select_cases(cases: list[dict], case_ids: tuple[str, ...] | None) -> list[dict]:
    """Select after whole-batch duplicate detection; reject incomplete test inputs."""
    if case_ids is None:
        return cases
    if not case_ids or len(set(case_ids)) != len(case_ids):
        raise ValueError("Case selection must be nonempty and unique")
    available = {case["case_id"]: case for case in cases}
    if set(case_ids) - available.keys():
        raise ValueError("Selected cases are absent from the generated corpus")
    return [available[case_id] for case_id in case_ids]
