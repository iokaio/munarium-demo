# SPDX-License-Identifier: Apache-2.0
"""Business checks use integer cents; generated prose cannot change these results."""

from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path


def integer(value: object, field: str) -> int:
    if isinstance(value, bool) or not re.fullmatch(r"[0-9]+", str(value)):
        raise ValueError(f"{field} must be a nonnegative integer")
    parsed = int(str(value))
    if parsed > 10**12:
        raise ValueError(f"{field} exceeds demo limit")
    return parsed


def load_cases(inputs: Path) -> list[dict]:
    cases = []
    for folder in sorted(inputs.glob("case-*")):
        pairs = [line.split("=", 1) for line in (folder / "invoice.txt").read_text().splitlines()]
        invoice = dict(pairs)
        if (
            len(pairs) != len(invoice)
            or invoice.get("fictional") != "true"
            or invoice.get("case_id") != folder.name
        ):
            raise ValueError("Invalid synthetic invoice header")
        if not re.fullmatch(r"case-[0-9]+", folder.name) or invoice["currency"] != "USD":
            raise ValueError("Unsupported case identifier or currency")
        for field in ("quantity", "unit_price_cents", "stated_total_cents"):
            invoice[field] = integer(invoice[field], field)
        for document in ("order", "receipt"):
            path = folder / f"{document}.json"
            invoice[document] = json.loads(path.read_text()) if path.exists() else None
            record = invoice[document]
            if record is not None:
                if record.get("fictional") is not True or record["order_id"] != invoice["order_id"]:
                    raise ValueError("Supporting record belongs to a different order")
                record["quantity"] = integer(record["quantity"], "quantity")
                if document == "order":
                    if record["currency"] != invoice["currency"]:
                        raise ValueError("Currency mismatch")
                    record["unit_price_cents"] = integer(
                        record["unit_price_cents"], "unit_price_cents"
                    )
        cases.append(invoice)
    if not cases:
        raise ValueError("No invoices found")
    counts = Counter(
        (case["supplier"].casefold().strip(), case["invoice_number"].casefold().strip())
        for case in cases
    )
    for case in cases:
        case["duplicate"] = (
            counts[(case["supplier"].casefold().strip(), case["invoice_number"].casefold().strip())]
            > 1
        )
    return cases


def calculate(case: dict) -> dict:
    exceptions = []
    computed = case["quantity"] * case["unit_price_cents"]
    if computed != case["stated_total_cents"]:
        exceptions.append("total_mismatch")
    if case["order"] is None:
        exceptions.append("missing_order")
    elif abs(case["unit_price_cents"] - case["order"]["unit_price_cents"]) > 50:
        exceptions.append("price_variance")
    receipt_status = "received"
    if case["receipt"] is None:
        exceptions.append("missing_receipt")
        receipt_status = "missing"
    elif case["receipt"]["quantity"] < case["quantity"]:
        exceptions.append("partial_receipt")
        receipt_status = "partial"
    if case["duplicate"]:
        exceptions.append("duplicate_invoice")
    return {
        "case_id": case["case_id"],
        "invoice_number": case["invoice_number"],
        "currency": case["currency"],
        "computed_total_cents": computed,
        "stated_total_cents": case["stated_total_cents"],
        "exceptions": sorted(exceptions),
        "receipt_status": receipt_status,
        "business_status": "review_required" if exceptions else "matched",
        "payment_authorized": False,
    }
