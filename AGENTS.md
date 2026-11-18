# DiscUtils contributor instructions

This repository is the LTRData fork of DiscUtils. For new or substantially extended filesystem and virtual disk format implementations, read [IMPLEMENTATION_GUIDELINES.md](IMPLEMENTATION_GUIDELINES.md) before planning or writing code.

Use its architecture map, interface checklist, discovery/registration guidance, and test expectations. Inspect the current checkout for exact API signatures and honor existing target frameworks. Do not copy older DiscUtils implementations indiscriminately or introduce parallel shared abstractions without justification.

Keep changes scoped to the requested work. In your proposed implementation and PR, distinguish implemented, unsupported and deferred features, and state which tests were actually run. Do not merge pull requests or change release/signing policies unless explicitly requested.
