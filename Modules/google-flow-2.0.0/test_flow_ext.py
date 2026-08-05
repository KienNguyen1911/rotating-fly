"""
Automated test verification suite for google_flow_ext extension package.
"""

import sys
import unittest
from typing import Any


class TestGoogleFlowExt(unittest.TestCase):

    def test_01_base_google_flow_unmodified(self) -> None:
        """Verify original base google_flow imports cleanly."""
        import google_flow.core.client as client_mod
        import google_flow.core.generator as gen_mod
        import google_flow.core.sdk as sdk_mod

        self.assertTrue(hasattr(client_mod, "FlowClient"))
        self.assertTrue(hasattr(gen_mod, "ImageGenerator"))
        self.assertTrue(hasattr(sdk_mod, "FlowSDK"))

    def test_02_google_flow_ext_subclassing(self) -> None:
        """Verify extended classes inherit from base classes."""
        from google_flow.core.client import FlowClient
        from google_flow.core.generator import ImageGenerator
        from google_flow.core.sdk import FlowSDK

        from google_flow_ext.client import ExtendedFlowClient
        from google_flow_ext.generator import ExtendedImageGenerator
        from google_flow_ext.sdk import ExtendedFlowSDK

        self.assertTrue(issubclass(ExtendedFlowClient, FlowClient))
        self.assertTrue(issubclass(ExtendedImageGenerator, ImageGenerator))
        self.assertTrue(issubclass(ExtendedFlowSDK, FlowSDK))

    def test_03_fastapi_app_routes(self) -> None:
        """Verify extended FastAPI app mounts all base + extended routes."""
        from google_flow_ext.api.app import app

        route_paths = []
        for r in app.routes:
            if hasattr(r, "path") and r.path:
                route_paths.append(r.path)
            elif hasattr(r, "original_router"):
                for sub_r in r.original_router.routes:
                    if hasattr(sub_r, "path") and sub_r.path:
                        route_paths.append(sub_r.path)
        
        # Base routes
        self.assertIn("/health", route_paths)
        
        # Extended routes
        self.assertIn("/v1/images/generations", route_paths)
        self.assertIn("/v1/images/edits", route_paths)
        self.assertIn("/v1/projects", route_paths)


if __name__ == "__main__":
    unittest.main()
