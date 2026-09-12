Tayson PS4 Receiver 1.0.5 test dependency

Component: libjbc object used for PS4 homebrew credential elevation
Source project: https://github.com/barisyild/airpsx
Pinned commit: aed9f7b54c45cd606969a6dceda5470528576803
Upstream path: toolchain/jbc.o
Git blob SHA-1: b28fe50deaad952cce14bed00fbf43500e65645c

The Receiver calls jbc lazily only when a package job is submitted;
normal startup/network PING remains on the safe-start path.
