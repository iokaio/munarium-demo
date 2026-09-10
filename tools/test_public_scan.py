# SPDX-License-Identifier: Apache-2.0
"""Disclosure-policy regressions use synthetic identifiers, never an estate inventory."""
import io
import unittest
import zipfile
from urllib.parse import quote
import check_public as scanner


class PublicScanTests(unittest.TestCase):
    def setUp(self):
        scanner.findings.clear()
        self.host = "sample-private" + ".eastus" + ".azurecontainerapps.io"

    def test_plain_encoded_and_utf16_hosts(self):
        for content in (self.host.encode(), self.host.replace(".", "%2E").encode(),
                        self.host.replace(".", "\\u002e").encode(), self.host.encode("utf-16-le")):
            with self.subTest(content_type=len(content)):
                scanner.findings.clear()
                scanner.scan("fixture.txt", content)
                self.assertTrue(scanner.findings)

    def test_nested_document_and_filename_redaction(self):
        nested = io.BytesIO()
        with zipfile.ZipFile(nested, "w") as archive:
            archive.writestr("docProps/core.xml", self.host)
        outer = io.BytesIO()
        with zipfile.ZipFile(outer, "w") as archive:
            archive.writestr("fixture.docx", nested.getvalue())
        scanner.scan("fixture.zip", outer.getvalue())
        self.assertTrue(scanner.findings)
        self.assertNotIn(self.host, scanner.redact(self.host + "/fixture.txt"))
        self.assertNotIn(self.host, scanner.redact(quote(self.host.replace(".", "%2E"))))

    def test_public_protocol_and_generic_example(self):
        scanner.scan("fixture.txt", b"munarium.ioka.io/v1 https://example.azurecr.io/demo")
        self.assertEqual(scanner.findings, [])


if __name__ == "__main__":
    unittest.main()
