# Round 190 — a slide's line break charges its trailing blank, and this tree hangs it

Measured 2026-09-28 against **LibreOffice 26.2.4.2** (`/opt/libreoffice26.2/program/soffice`),
corpus `/home/user/sample-files`, `SOURCE_DATE_EPOCH=0`.

The seat is task #23, `slides/done-012/pptx/Intersil_Italy_CAN_Bus_Transceiver_Presentation_Final.pptx`,
which the r186 chart sweep put at 18.08 `diff%`. **That figure is not a chart's.** The deck holds
one chart part and it is on slide 30, which scores 5.38; the 18.08 is slide **37**, a bulleted
text slide, and 15.91 is slide 13. A sweep that ranks a document on its worst page attributes
that page to whatever the document was selected for, and three task descriptions in this queue
have now turned out stale for reasons of that shape.

## What the page shows

A blind reading of the composed pair (`page-vision`, a fresh subagent, no repo access) named two
differences and got both right: the reference wraps bullet 3 one word earlier and needs three
lines where we need two, and **our leading is 9-11% looser at the same glyph size**. It also
named the right candidate for the second — *"`normAutofit` `lnSpcReduction` honoured by one side
and ignored by the other … the reference's text is the taller content (13 lines) yet the tighter
spacing"*.

The operators give both exactly. Baseline pitch: ours **31.27 pt**, the reference's **28.15**, at
25.99 pt in both — and `31.27 × 0.9 = 28.14`. That 0.9 is `constScaleLevels[0]`'s fourth column
(`editeng/source/editeng/impedit3.cxx`:286-300), whose first column is `1.000`: the reference's
first answer to an overflow is tighter leading at full size, applied as
`nHeight = round(GetHeight() * fSpacingY)` in the `InterLineSpaceRule::Off` arm (`:1584-1600`).
So **the reference is in autofit and we are not**, and the reason is the extra line.

## The extra line is a trailing blank, and the experiment is one attribute

Identical lines measure identically on the two sides, so it is not metrics — over five lines the
widths agree to **0.6 pt**:

| line | ours | reference |
|---|---:|---:|
| `applications and single point failures must be avoided` | 798.01 | 797.68 |
| `will use two CAN transceivers in parallel` | 591.77 | 591.57 |
| `transceiver malfunctions` | 365.60 | 365.53 |
| `system supply rail > 2MΩ (typical)` | 505.84 | 506.44 |
| the title | 363.88 | 363.46 |

The body placeholder is `<a:off x="609599"/><a:ext cx="11012681"/>` — x 48.00 pt, **right edge
915.14**. Our accepted line `• One transceiver will be active while the other transceiver` ends
at **906.82**; the reference refuses it and ends at 740.57 after `other `.

`widths.py` rewrites `cx` alone and asks the reference where it breaks:

| placeholder right edge | the reference's line | its right edge |
|---:|---|---:|
| 907.0 | `…while the other ` | 740.57 |
| 915.0 | `…while the other ` | 740.57 |
| 915.14 (as authored) | `…while the other ` | 740.57 |
| 915.6 | `…the other transceiver ` | 915.70 |
| 916.14 | `…the other transceiver ` | 915.70 |
| 924.0 | `…the other transceiver ` | 915.70 |

**The boundary is between 915.0 and 915.6, and the candidate line's own extent is 915.70.**
Without its trailing blank that line reaches 906.82, which is below every edge in the table at
which the reference still breaks — so *"the blank is hung past the end of the line"*, which is
what this tree does, is refuted, and the blank is charged against the break to within half a
point. The blank is worth **8.88 pt** here (the same line with and without it, 751.98 against
760.75 on bullet 1).

**And the two differences are one.** At a right edge of 916.14 the reference draws bullet 3 on
two lines like ours and its baselines are **241.97 and 274.68 — ours exactly**. So the leading
is not a second defect: one extra line tips the body past its box, the box autofits, and the
whole slide reads as displaced.

## Where it would go, and why it is not done here

The wrap is `Paperless.Text/Layout/ParagraphLayouter`, which all three tracks share, and Writer
genuinely does hang a trailing blank — so this cannot be a global change. It needs a mode the
slides path (and Calc's shape text, which is the same EditEngine) opts into, and a slides-track
confinement sweep behind it, because the rule is a tie-breaker that fires wherever a line's next
word lands within one space of the limit. Filed as task #28.
