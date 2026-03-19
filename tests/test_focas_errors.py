from __future__ import annotations

import unittest
from unittest import mock

from src.fanuc_collector.focas import FocasClient, focas_error_text


class FocasErrorTest(unittest.TestCase):
    def test_focas_error_text_for_nodll(self) -> None:
        self.assertEqual(focas_error_text(-15), "EW_NODLL(-15): FOCAS 运行时缺少配套 DLL")

    def test_connect_error_contains_context(self) -> None:
        client = FocasClient(
            dll_path="vendor/Fwlib32.dll",
            ip="192.168.91.46",
            port=8193,
            timeout_sec=10,
            running_modes=[1, 2, 3],
        )

        with mock.patch.object(client, "_probe_tcp_port", return_value="ok"):
            with mock.patch.object(client, "_dependency_check_summary", return_value="Fwlib32.dll=ok, fwlibe1.dll=ok"):
                message = client._format_connect_error(-15)

        self.assertIn("function=cnc_allclibhndl3", message)
        self.assertIn("error=EW_NODLL(-15): FOCAS 运行时缺少配套 DLL", message)
        self.assertIn("target=192.168.91.46:8193", message)
        self.assertIn("dependency_check=Fwlib32.dll=ok, fwlibe1.dll=ok", message)


if __name__ == "__main__":
    unittest.main()
