# Velkhana Slice collaboration rules

For meaningful feature work, use three specialist agents:

1. An architecture/reference agent validates the design against the decoded Monster Hunter World reference data.
2. An implementation agent owns the scoped code and content changes.
3. A review/validation agent independently checks fidelity, regressions, tests, and the generated playable build.

The primary agent integrates their findings, resolves discrepancies, and remains responsible for the final result.

Create an intentional Git commit after each meaningful change has been implemented and verified. Do not create checkpoint commits for read-only investigation or trivial edits.
