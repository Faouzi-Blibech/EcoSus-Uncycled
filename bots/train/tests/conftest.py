import sys
from pathlib import Path

import pytest

FAKE_HOST = Path(__file__).with_name("fake_host.py")


@pytest.fixture
def fake_host_cmd():
    """Host command prefix; UncycledVecEnv appends the protocol flags."""
    return [sys.executable, str(FAKE_HOST)]
