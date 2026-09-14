#!/usr/bin/env python3
"""Behavioral contracts for readiness based on real product log samples."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("macos_gate", Path(__file__).with_name("macos-elicitation-consent-smoke.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def sample(sequence, x=10, enabled=True):
    return f"NativeElicitationProbe sample seq={sequence}\nNativeElicitationProbe button seq={sequence} action=decline enabled={enabled} x={x} y=20 width=70 height=33 rootHeight=640 scale=1"


class ReadinessTests(unittest.TestCase):
    def test_repeated_reads_of_one_frame_never_mean_stable(self):
        reader = module.StableNativeButtonSample("decline")
        for _ in range(8):
            self.assertIsNone(reader.observe(sample(1)))

    def test_three_fresh_identical_frames_are_ready(self):
        reader = module.StableNativeButtonSample("decline")
        self.assertIsNone(reader.observe(sample(1)))
        self.assertIsNone(reader.observe(sample(2)))
        self.assertEqual(reader.observe(sample(3)), ("10", "20", "70", "33", "640", "1"))

    def test_geometry_change_restarts_readiness(self):
        reader = module.StableNativeButtonSample("decline")
        reader.observe(sample(1))
        reader.observe(sample(2))
        self.assertIsNone(reader.observe(sample(3, x=50)))
        self.assertIsNone(reader.observe(sample(4, x=50)))
        self.assertIsNotNone(reader.observe(sample(5, x=50)))

    def test_disabled_frame_restarts_readiness(self):
        reader = module.StableNativeButtonSample("decline")
        reader.observe(sample(1))
        reader.observe(sample(2))
        self.assertIsNone(reader.observe(sample(3, enabled=False)))
        self.assertIsNone(reader.observe(sample(4)))
        self.assertIsNone(reader.observe(sample(5)))
        self.assertIsNotNone(reader.observe(sample(6)))

    def test_old_frames_do_not_advance_readiness(self):
        reader = module.StableNativeButtonSample("decline")
        reader.observe(sample(5))
        self.assertIsNone(reader.observe(sample(4)))
        self.assertIsNone(reader.observe(sample(6)))
        self.assertIsNotNone(reader.observe(sample(7)))


if __name__ == "__main__":
    unittest.main()
