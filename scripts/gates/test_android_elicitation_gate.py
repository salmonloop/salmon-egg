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


class NativeFormInputTests(unittest.TestCase):
    def device(self, value, focused=True, label="Acceptance answer"):
        root = ET.Element("hierarchy")
        field = ET.SubElement(root, "node", package=gate.PACKAGE, bounds="[0,0][200,60]",
                              enabled="true", **{"content-desc": label, "class": "android.view.ViewGroup"})
        ET.SubElement(field, "node", package=gate.PACKAGE, bounds="[0,20][200,60]",
                      enabled="true", focused=str(focused).lower(), text=value,
                      **{"class": "android.widget.EditText"})
        device = gate.AndroidDevice("emulator-test", Path("unused"))
        device.tree = Mock(return_value=root)
        return device

    def test_partial_input_is_not_committed(self):
        self.assertFalse(self.device("native-form-answe").input_matches("Acceptance answer", "native-form-answer", focused=True))

    def test_complete_value_requires_the_requested_editor_and_focus(self):
        self.assertTrue(self.device("native-form-answer").input_matches("Acceptance answer", "native-form-answer", focused=True))
        self.assertFalse(self.device("native-form-answer", focused=False).input_matches("Acceptance answer", "native-form-answer", focused=True))
        self.assertFalse(self.device("native-form-answer", label="Different field").input_matches("Acceptance answer", "native-form-answer"))

    def test_value_remains_verifiable_after_focus_moves(self):
        self.assertTrue(self.device("native-form-answer", focused=False).input_matches("Acceptance answer", "native-form-answer"))


if __name__ == "__main__":
    unittest.main()
