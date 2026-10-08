# Samurai Play Mode checkpoint

Basic24/Report.txt preserves the earlier completed 24-case suite.
RecoveryBoundaryFailure preserves the initial 5.514 mm getup-entry displacement
against the unchanged 2 mm tolerance. The grounded local-position blend fixes
that boundary; all four measured boundaries now report 0.000000 m.

Report.txt records the latest 40-case extended run. Cases 1–32 passed, including
range guards, queued old/new/old actions, lethal outcomes and reset, precontact
interrupts, reset after contact, and receiver disable after contact across both
avatars and directions. Natural cleanup observes actual effect/voice expiry,
then disarms its observer before explicit reset between cases.

Case 33 failed during prone pause/resume: the particle clock for
StrongSpreadingSmoke advanced from 0.612848 to 0.727778 while the harness expected
it to remain paused. Cases 34–40 did not run. The launcher restored the previous
play scene and removed its temporary copy. This failure remains unresolved.

The extended suite is not accepted. The terminal FAIL line is authoritative even
though the report retains its initial RUNNING header. Screenshots are frame
evidence; normal-speed choreography and dedicated audio review remain pending.
