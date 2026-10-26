---
name: grill
description: Grill the decision-maker on a plan or decision in batched rounds until nothing is left open, keeping docs/glossary.md and decision records current as answers settle. Use on "grill me", before a BMAD brief or PRD, or when a decision has open branches.
---

# Grilling

Adapted from the `grilling` and `domain-modeling` skills in the [skills](https://github.com/mattpocock/skills) repository (MIT).

## Rounds

1. **Ask only the frontier.** A round contains only questions whose prerequisites are already settled. A question that depends on another open one waits for a later round.
2. **Number every question and end it with the recommended answer,** worded so that "yes" accepts it. The reply is "all recommended" or names the ones that change.
3. **Look facts up directly** (code, docs, research, sub-agents). Ask only for decisions.
4. **After each round,** reopen any branch an answer changed, then build the next frontier.
5. **Stop only when the frontier is empty.** Then restate the shared understanding in a few lines and get confirmation before anything is acted on.

## While grilling

- **Glossary.** When a term settles, add or update it in `docs/glossary.md`: the term, a one-line definition in domain language (no implementation detail), and an `_Avoid_` list of synonyms. Challenge any term that conflicts with the glossary as soon as it comes up.
- **Decision records.** Write one only when a decision passes all three tests: hard to reverse, surprising without context, and the result of a real trade-off. One paragraph: the decision, the options rejected and why.

## Output

Grilling itself writes only the glossary and any decision records. Its answers feed the next BMAD skill (brief or PRD) as input.
