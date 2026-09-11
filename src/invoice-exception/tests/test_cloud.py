# SPDX-License-Identifier: Apache-2.0
"""One real-provider acceptance test per assigned invoice, run explicitly by cloud-test."""

import json
import os
from pathlib import Path

import pytest

from invoice_demo.cloud_plan import CLOUD_CASES
from invoice_demo.quality import assess


@pytest.mark.parametrize("case_id", CLOUD_CASES.get(os.environ.get("INVOICE_CLOUD_PROVIDER"), ()))
def test_cloud_invoice_acceptance(case_id):
    config = json.loads(Path("/credentials/query.json").read_text())
    assert config["provider"] == os.environ["INVOICE_CLOUD_PROVIDER"]
    report = assess(Path(os.environ["INVOICE_CLOUD_OUTPUT"]), Path("/oracle"), config, (case_id,))
    assert report["real_model"]
    assert not report["failures"], report["failures"]
