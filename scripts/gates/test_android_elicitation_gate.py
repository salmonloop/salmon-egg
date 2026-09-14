"""Checks that native observation failures cannot look like successful consent cleanup."""

import importlib.util
from pathlib import Path
import unittest
from unittest.mock import Mock
import xml.etree.ElementTree as ET


spec = importlib.util.spec_from_file_location(
    "android_elicitation_gate", Path(__file__).with_name("run-android-elicitation-gate.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class ConsentCleanupObservationTests(unittest.TestCase):
    def device(self, xml, foreground=True):
        device = gate.AndroidDevice("emulator-test", Path("unused"))
        device.tree = Mock(return_value=ET.fromstring(xml))
        device.foreground = Mock(return_value=foreground)
        return device

    def snapshot(self, label=None, package=gate.PACKAGE, title_bounds="[0,0][200,30]"):
        root = ET.Element("hierarchy")
        ET.SubElement(root, "node", package=package, bounds=title_bounds,
                      **{"content-desc": "Toggle sidebar"})
        if label:
            ET.SubElement(root, "node", package=package, bounds="[0,40][200,80]",
                          **{"content-desc": label})
        return ET.tostring(root)

    def test_missing_or_wrong_native_surface_is_not_cleanup(self):
        unrelated = ET.Element("hierarchy")
        ET.SubElement(unrelated, "node", package=gate.PACKAGE, bounds="[0,0][200,30]",
                      **{"content-desc": "Unrelated screen"})
        for xml, foreground in [
            ("<hierarchy />", True),
            (ET.tostring(unrelated), True),
            (self.snapshot(package=gate.CHROME), True),
            (self.snapshot(title_bounds="[0,0][0,0]"), True),
            (self.snapshot(), False),
        ]:
            with self.subTest(xml=xml, foreground=foreground):
                self.assertFalse(self.device(xml, foreground).product_controls_absent("Open again"))

    def test_existing_consent_details_prevent_cleanup(self):
        for label in ("Open again", "https://consent.example/test"):
            with self.subTest(label=label):
                device = self.device(self.snapshot(label))
                self.assertFalse(device.product_controls_absent("Open again", "https://consent.example/test"))

    def test_product_shell_without_consent_details_proves_cleanup(self):
        device = self.device(self.snapshot())
        self.assertTrue(device.product_controls_absent("Open again", "https://consent.example/test"))
        device.tree.assert_called_once()
        device.foreground.assert_called_once_with(gate.PACKAGE)


if __name__ == "__main__":
    unittest.main()
