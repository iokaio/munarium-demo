# SPDX-License-Identifier: Apache-2.0
"""Seeded fictional inputs and an independent, separately mounted test oracle."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

GENERATOR_VERSION = 1
RULES = {
    "partial_receipt": "Quantity billed above quantity received requires review; never confirm unreceived goods.",
    "missing_receipt": "A missing receipt means insufficient evidence of delivery; request the receipt and do not confirm delivery.",
    "price_variance": "An invoice unit price differing from the purchase order by more than 50 cents requires review.",
    "total_mismatch": "The invoice stated total must equal quantity times unit price in integer cents.",
    "missing_order": "A missing purchase order means insufficient evidence of an agreed price; request the order.",
    "duplicate_invoice": "The same supplier and invoice number in multiple submissions requires duplicate review.",
    "matched": "A matched record has an order and receipt, consistent arithmetic, and no detected exception. Payment still requires human authorization.",
}


def encode(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode()


def save(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(encode(value))


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def generate(inputs: Path, oracle: Path, seed: int = 111, groups: int = 3) -> dict:
    """Refuse existing nonempty output directories; never destroy someone's fixtures."""
    if (
        inputs.resolve() == oracle.resolve()
        or inputs.resolve() in oracle.resolve().parents
        or oracle.resolve() in inputs.resolve().parents
    ):
        raise ValueError("Input corpus and private oracle must be separate directories")
    if groups < 1 or groups > 100:
        raise ValueError("groups must be between 1 and 100")
    for directory in (inputs, oracle):
        if directory.exists() and any(directory.iterdir()):
            raise ValueError("Generate into empty directories")
    inputs.mkdir(parents=True, exist_ok=True)
    oracle.mkdir(parents=True, exist_ok=True)
    expected = {}
    kinds = [
        "matched",
        "partial_receipt",
        "missing_receipt",
        "price_variance",
        "total_mismatch",
        "missing_order",
    ] * groups + ["duplicate_invoice"] * 2
    for index, kind in enumerate(kinds, 1):
        case = f"case-{index:03}"
        folder = inputs / case
        folder.mkdir()
        price = 1000 + int(digest(f"{seed}:{index}".encode())[:6], 16) % 9000
        billed_price = price + (100 if kind == "price_variance" else 0)
        total = billed_price * 10 + (100 if kind == "total_mismatch" else 0)
        number = "DUPLICATE-001" if kind == "duplicate_invoice" else f"INV-{index:03}"
        fields = {
            "fictional": "true",
            "case_id": case,
            "supplier": "Fictional Acme",
            "invoice_number": number,
            "order_id": f"PO-{index:03}",
            "currency": "USD",
            "quantity": "10",
            "unit_price_cents": str(billed_price),
            "stated_total_cents": str(total),
            "date": "2026-01-15",
        }
        (folder / "invoice.txt").write_bytes(
            "".join(f"{key}={value}\n" for key, value in fields.items()).encode()
        )
        if kind != "missing_order":
            save(
                folder / "order.json",
                {
                    "fictional": True,
                    "order_id": fields["order_id"],
                    "quantity": 10,
                    "unit_price_cents": price,
                    "currency": "USD",
                },
            )
        if kind != "missing_receipt":
            save(
                folder / "receipt.json",
                {
                    "fictional": True,
                    "order_id": fields["order_id"],
                    "quantity": 6 if kind == "partial_receipt" else 10,
                },
            )
        policy = (
            "# Fictional purchasing policy — revision 1\n\n"
            + "\n\n".join(f"{key}: {rule}" for key, rule in RULES.items())
            + "\n"
        )
        (folder / "policy.md").write_bytes(policy.encode())
        expected[case] = {
            "exceptions": [] if kind == "matched" else [kind],
            "computed_total_cents": billed_price * 10,
            "stated_total_cents": total,
            "receipt_status": "missing"
            if kind == "missing_receipt"
            else "partial"
            if kind == "partial_receipt"
            else "received",
            "required_source_suffix": "/policy.md",
        }
    manifest = {
        "generator_version": GENERATOR_VERSION,
        "seed": seed,
        "groups": groups,
        "logical_date": "2026-01-15",
        "locale": "en-US",
        "timezone": "UTC",
        "fictional": True,
        "case_count": len(kinds),
        "files": {
            str(path.relative_to(inputs)).replace("\\", "/"): digest(path.read_bytes())
            for path in sorted(inputs.rglob("*"))
            if path.is_file()
        },
    }
    save(inputs / "manifest.json", manifest)
    save(oracle / "expected.json", expected)
    return manifest


def verify(inputs: Path) -> dict:
    manifest = json.loads((inputs / "manifest.json").read_text())
    actual = {
        path.relative_to(inputs).as_posix(): digest(path.read_bytes())
        for path in sorted(inputs.rglob("*"))
        if path.is_file() and path != inputs / "manifest.json"
    }
    if actual != manifest["files"]:
        raise ValueError(
            "Fixture hashes differ from manifest; regenerate or explicitly version the inputs"
        )
    return manifest
