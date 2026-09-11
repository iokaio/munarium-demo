# SPDX-License-Identifier: Apache-2.0
import unittest
from demo_preflight import GIB, capacity
from summarize_demo import size


class CapacityTests(unittest.TestCase):
    def test_docker_binary_and_decimal_units_are_distinct(self):
        self.assertEqual(size("1.5MiB"), 1572864)
        self.assertEqual(size("1.5MB"), 1500000)
        with self.assertRaises(ValueError):
            size("unknown")

    def test_resource_shortages_fail_independently(self):
        for field in range(4):
            values = [8 * GIB, 30 * GIB, 3 * GIB, 4]
            values[field] = 0
            report = capacity("default", "shift-handover", *values)
            self.assertFalse(report["passed"])
            self.assertEqual(len(report["failures"]), 1)

    def test_stress_and_matrix_require_more_memory(self):
        self.assertTrue(capacity("default", "shift-handover", 6 * GIB, 30 * GIB, 3 * GIB, 4)["passed"])
        for profile, demo in [("stress", "shift-handover"), ("default", "inventory-replenishment")]:
            self.assertEqual(capacity(profile, demo, 6 * GIB, 30 * GIB, 3 * GIB, 4)["failures"], ["memory_available_bytes"])


if __name__ == "__main__":
    unittest.main()
