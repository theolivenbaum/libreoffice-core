# Handover — Paperless, round of 2026-08-19

Branch: **`claude/paperless-odf-phase-1-rnyzcu`** (the deliverable; `main` is not used until the
port is finished). Corpus repo `sample-files` is a separate checkout at
`/c/sandbox/workdir/sample-files` and has its own commits.

Read `dotnet/CLAUDE.md` first — it is dense with traps that have each cost a round. This file only
covers what changed and what to do next.

## Where the corpus stands

Measured against reference LibreOffice **26.2.4.2**, sweeping every batch:

| track | passing | of | this round |
|---|---:|---:|---|
| words | 300 | 337 | +5 (chartset 69→74) |
| sheets | 265 | 307 | +13 (chartset 57→70) |
| slides | 198 | 302 | +0 — see the `cap` note below |
| **total** | **763** | **946** | |

`MANIFEST.tsv` is the authority on status and is now refreshed from these sweeps.

## What landed

| commit | what |
|---|---|
| `d97a6578979` | `batch-check.sh` filtered on 13 extensions where 34 are in scope; two `.xlsm` were silently unmeasured |
| `259700da99b` | DrawingML `a:rPr/@cap` — text capitalisation, **wins no gate verdict** |
| `8d4309e566a` | `headerFooter/@differentFirst` — first printed page draws no header/footer |
| `803edbd51e1` | floating VML shapes and their text boxes are drawn |

Corpus: `2f0aa9a` (300 documents added), `869af31`, `8c51192`, `7d737db`.

## Three things that will mislead you if you don't read them

**1. A real fix that moves no verdict.** `a:rPr/@cap` was a genuine defect — the reference drew
`LOREM IPSUM` where we drew `Lorem Ipsum` — and fixing it moved **zero** gate verdicts, because the
gate counts *words* and upper-casing a word does not change how many there are. I predicted 20
documents and got none. Do not go looking for the missing verdicts. Character identity over the
100 downloaded decks went 50/100 → 67/100, which is the only measurement that can see it.

**2. 60 of the 88 slide failures are a measurement ceiling, not a defect.** Those decks carry
`spc="150"` letter spacing from their masters; LibreOffice positions each glyph separately enough
that `pdftotext` splits inside words — `2-Way` extracts as `2` + `-W` + `ay` — so the reference is
credited three tokens where we get one. 1585 phantom words. **Our output is the better one.** They
are filed `kind=ceiling`. No code change wins them. The honest reading of slides is 72 of 100
correct in content, not 12.

**3. Stored status decays.** `MANIFEST.tsv` was 41 rows behind a fresh sweep, all understating
progress, and four of them were named open problems in the task list that earlier commits had
already closed (`orbus_togaf_tool_csq.xls` 75/75, `sectors-defense-and-aerospace.xlsx` 449/449,
`grants-2005.xls` 201/201, `SIL_TDB648.xlsx` 90/90). **Re-sweep before trusting the manifest.**

## The method that found every fix this round

Not the gate. For each failing document, strip **all** whitespace from both `pdftotext`
extractions and compare the characters that remain. That separates "we drew different text" from
"we drew the same text and `pdftotext` tokenised it differently", and it is what turned an
undifferentiated pile of 162 failures into four named mechanisms. The script is
`/tmp/.../scratchpad/charstream.sh` — reproduce it, it is 60 lines.

Then look for clusters in the *size* of the gap. Six sheets workbooks failing by exactly 4 words
was `Page 1 of 1`. Seven words documents at exactly 0 words was floating VML.

## What to do next, in the order I would do it

1. **`[CELLRANGE]` chart data labels** (task #79). We draw the literal placeholder where the
   reference draws the values. Needs the `c15:datalabelsRange` extension resolved. Reach unmeasured
   — grep the corpus for `c15:datalabelsRange` first.
2. **The 28 slides `kind=text` documents.** Page counts agree, characters genuinely differ, cause
   unknown. Use the vision skill; look before theorising.
3. **The 30 sheets `kind=text` documents.** None is a ceiling — every one is a real content or
   layout difference, which makes this the highest-yield pool left.
4. **`cap="small"`** (7 decks) and **`differentOddEven`** (1 workbook) are deliberately unimplemented.
   Both need LibreOffice's behaviour measured with a probe before anything is written. Do not guess.
5. **Carlito advance accumulation** (task #49) still underlies the three `metrics-001` documents at
   +2 pages. It is architectural — reproducing FreeType's hinted advance — not a rounding patch.

## Operational rules, every one of which has bitten

- **Never `git add -A`.** This mount reports symlink size as 0, so git sees 56 symlinks as emptied
  and staging everything replaces them with empty files. Stage explicit paths, then verify
  `git ls-files -s <paths> | awk '$1=="120000"'` prints nothing.
- **Never `git stash`** — the stash stack is repository-global across worktrees.
- **Restore with `cp` + `touch`, never `mv`** — `mv` keeps the old mtime and MSBuild skips the
  rebuild, so the binary silently keeps your experiment.
- **Never pipe `batch-check.sh` into `head`/`tail`** — SIGPIPE kills a worker and the run writes
  fewer rows while the summary still looks plausible.
- **A sweep and a rebuild must never overlap.**
- **One soffice profile per FILE**, keyed by the file and not by a worker-slot index — `wait -n`
  tells you a slot freed, not which one. I hit this twice in one session.
- Background-task completion notifications in this harness **fire early**. Poll for the `TOTAL`
  line; do not trust the notification.
- `~60` orphaned `wt-*` directories under `/c/sandbox/workdir` are dead weight (~16 GB) from
  earlier sessions — plain directories, not worktrees, safe to delete.

## Parallelism

The session hit its **200-subagent cap**, so this round ran the three streams sequentially rather
than as parallel agents. Worktrees for that were created and then removed. A fresh session gets a
fresh budget: `git worktree add -b wt-<stream> /c/sandbox/workdir/wt-<stream> HEAD`, one per stream,
so no agent's build ever lands under another's sweep. Checkout takes a few minutes on this tree.
