#!/usr/bin/env python3

import importlib.util
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("supabase_inventory.py")
SPEC = importlib.util.spec_from_file_location("supabase_inventory", MODULE_PATH)
assert SPEC and SPEC.loader
INVENTORY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(INVENTORY)


class SupabaseInventoryTests(unittest.TestCase):
    def test_generated_public_assets_are_not_scanned_as_source(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "src" / "client.ts"
            generated = root / "ios" / "App" / "public" / "assets" / "index.js"
            source.parent.mkdir(parents=True)
            generated.parent.mkdir(parents=True)
            source.write_text(
                'const url = import.meta.env.VITE_SUPABASE_URL;\n',
                encoding="utf-8",
            )
            generated.write_text(
                'const url = "https://abcdefghijklmnopqrst.supabase.co";\n',
                encoding="utf-8",
            )

            files = INVENTORY.source_files(root, INVENTORY.APP_SOURCE_SUFFIXES)

            self.assertIn(source, files)
            self.assertNotIn(generated, files)
            result = INVENTORY.inventory_app(root)
            self.assertEqual(result["detected_managed_project_refs"], [])


if __name__ == "__main__":
    unittest.main()
