"""Testes isolados do protocolo 09R, sem depender do dataset do projeto."""
import importlib.util
import sys
import unittest
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.datasets import make_classification

SCRIPT_DIR = Path(__file__).resolve().parents[1] / "src"
sys.path.insert(0, str(SCRIPT_DIR))
spec = importlib.util.spec_from_file_location("experiment_09r", SCRIPT_DIR / "09r_repeated_cv_depth_stability.py")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class RepeatedCVTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        x, labels = make_classification(
            n_samples=90, n_features=49, n_informative=12, n_redundant=0,
            n_classes=3, n_clusters_per_class=1, random_state=42
        )
        cls.x = pd.DataFrame(x)
        cls.y = np.array(mod.CLASSES)[labels]
        cls.params = {"n_estimators": 8, "max_depth": 12, "max_features": "sqrt",
                      "min_samples_split": 2, "min_samples_leaf": 1}

    def test_paired_repeated_oof_and_reproducibility(self):
        r1 = mod.evaluate_depths(self.x, self.y, self.params, [5, 12], folds=3, repeats=2, seed=42)
        r2 = mod.evaluate_depths(self.x, self.y, self.params, [5, 12], folds=3, repeats=2, seed=42)
        self.assertEqual(r1, r2)
        for depth in (5, 12):
            self.assertEqual(len(r1[str(depth)]["folds"]), 6)
            self.assertEqual(len(r1[str(depth)]["repeats"]), 2)
            for repeat in r1[str(depth)]["repeats"]:
                self.assertEqual(sum(sum(row) for row in repeat["confusion_matrix"]), 90)
                self.assertEqual(sum(c["support"] for c in repeat["by_class"].values()), 90)
        summary = mod.summarize(r1, [5, 12])
        self.assertIn("macro_f1", summary["5"]["metrics"])
        paired = mod.paired_differences(r1, [5, 12])
        self.assertEqual(len(paired["5_vs_12"]["macro_f1"]["values_by_repeat"]), 2)

    def test_invalid_cv(self):
        with self.assertRaises(ValueError):
            mod.evaluate_depths(self.x, self.y, self.params, [5], folds=1)


if __name__ == "__main__":
    unittest.main()
