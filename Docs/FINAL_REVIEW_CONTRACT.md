# Final code review contract

Scope: formatting and focused static review of root-owned Editor tooling only.
Files permitted: Assets/Editor/BattlePresentationWorkbench.cs and BattlePresentationWorkbench.Audit.cs.
Preserve all public API signatures, request JSON keys, whitelist entries, file paths and scene preservation behavior.
Preserve captureEarliest delay handling. No behavior changes without a concrete bug and a report explaining it.
No Unity jobs, scene edits, asset setup or runtime changes. No other files changed.
Follow Assets/AGENTS.md formatting: one statement per line, approximately 120 columns, under 300 lines per file.
Use current Unity reference compiler via /tmp/compile_battle_polish.py if possible; report once after complete.
