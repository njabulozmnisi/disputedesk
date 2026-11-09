# ui-raise-review: postmortem for a second fix round

Trigger (AGENTS.md, Continuous improvement): a second fix round, opened 2026-11-08. The first such postmortem is ui-tokens-icons-postmortem.md; its proposals stand.

- Round 1 (review): the review screen's masked number lost its screen-reader form; the spec named only the visible text.
- Round 2 (QA): a fixed rem label column collapsed the value column under text-only scaling. The builder's browser pass checked widths and zoom, not text scaling; the spec fixed "9rem at every width".
- Proposed, with the earlier lines (a skill change asks): the frontend builder's headless pass also sets the root font size to 200% at 320px; the pre-build review flags fixed rem or px widths in a criterion against SC 1.4.4 and 1.4.10.
