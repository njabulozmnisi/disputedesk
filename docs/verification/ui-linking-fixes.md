# ui-linking: fix round 1

Base: `2f897c37b3b58b11119c6126123213f98defeb43` with ui-tokens-icons (fix round 3) and ui-notices-states (fix round 1), uncommitted. The story so far (frontend and gateway parts) is `ui-linking-r1.patch`.

| # | Stage | Severity | File | Finding | Fix |
|---|---|---|---|---|---|
| 1 | security M1, review M1 | medium | `frontend/src/features/linking/linkSchema.ts:45` (`isOverlongCode`) | A paste of more than 6 digits followed by a non-space character (`"4815167\n"`, `"4815167\r\n"`, `"4815167\t"`, `"481516 7"`, `"4815167a"`) is sliced to `481516` by input-otp and accepted with no error; Verify then spends an attempt, against the 2026-11-08 decision. | Refuse when the pasted text holds more than 6 digits: count the digits (`(text.match(/[0-9]/g) ?? []).length > codeLength`), in the one shared check used by the `pasteTransformer` and `onPaste`. A paste with exactly 6 digits plus other characters (`"481516a"`, `"481516\n"`) stays accepted. Tests: `"4815167\n"`, `"4815167\r\n"`, `"481516 7"` and `"4815167a"` refused (cells unchanged, error, no request); `"481516\n"` and `"481516a"` accepted. |
| 2 | review M2 | medium | `frontend/src/features/linking/OtpScreen.tsx:213` | The refused-paste error appears with no role, live region or focus move, so screen-reader users are not told (WCAG 2.2 SC 4.1.3). | Announce `linkingMessages.code` through the existing `useAnnounce` when a paste is refused; test it. |
| 3 | verify M1 | medium | `OtpScreen.tsx` (ErrorSummary mount) | After an earlier submit (`summaryRequests > 0`), a refused paste's `setError` mounts the ErrorSummary, which focuses itself; the spec says field error only, no summary or focus move until Verify. | A paste-caused error shows only under the field; the summary keeps showing errors from the last Verify only. Test the sequence: invalid submit, `otp-incorrect`, then a 7-digit paste: focus stays in the field, no new summary. |
| 4 | verify M2 | medium | `frontend/src/components/ui/button.tsx:11`, `disabled.test.tsx:23` | Disabled buttons use `disabled:border-transparent`; criterion 6 says a disabled control has `muted` fill, `border` edge and `muted-foreground` text. | Use the `border` token edge when disabled; fix the test to assert it. |

Lows (review L1 to L4) go to `lows.md` at merge. The AD-23 text is written by the supervisor before the commit.
