import os

from setuptools import Distribution, setup
from setuptools.command.bdist_wheel import bdist_wheel


class VesselDistribution(Distribution):
    def has_ext_modules(self):
        return bool(os.environ.get("VESSEL_WHEEL_PLATFORM")) or super().has_ext_modules()


class VesselWheel(bdist_wheel):
    """A bundled library is platform-specific and has no Python extension ABI."""

    def get_tag(self):
        if platform := os.environ.get("VESSEL_WHEEL_PLATFORM"):
            return "py3", "none", platform
        return super().get_tag()


setup(distclass=VesselDistribution, cmdclass={"bdist_wheel": VesselWheel})
