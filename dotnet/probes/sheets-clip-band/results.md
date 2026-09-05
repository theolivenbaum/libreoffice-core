# Where a header or footer band is clipped

Round `clip`, 2026-09-05. Environment: this container, `/usr/bin/soffice` **24.2.7.2** and
`/opt/libreoffice26.2/program/soffice` **26.2.4.2** (TDF tarball, its 33 duplicate font files
moved aside), `fc-match "DejaVu Sans"` → DejaVu, Carlito and Caladea installed.

## What was asked

`ScPrintFunc::PrintHF` sets one clip region before it draws a band's three areas
(`sc/source/ui/view/printfun.cxx:1870`) and every area goes through it. `SheetPageDecoration`
already knew that rectangle existed — it used it to decide whether an area is drawn **at all** —
and read `ImpEditEngine::DrawText_ToPosition` as leaving a partly-overlapping area unclipped.
That reading was wrong. `editeng/source/editeng/impedit3.cxx:3367-3389` has three branches, not
two: no overlap draws nothing, wholly inside draws unwrapped, and **anything else is embedded in
a `MaskPrimitive2D` of the clip polygon** — which keeps every line in the primitive tree, and
cuts all of them geometrically when they are rendered.

So the question is where the rectangle is, and it is readable straight out of the reference's own
content stream as a `re W* n` rather than inferred from ink. `probe.py` prints it for both
references and for us.

## Result

`sheets/done-011/xlsx/FY2023-AIP-grants.xlsx`, page 3, in top-down page points. Its header is
three centred lines (`&C…\n…\n…`), its footer one; its print scale is 43 %; its page is
792 × 612; its margins are 0.25 in left and right, 0.75 in top and bottom, 0.3 in header and
footer.

| side | top | height | left | width |
|---|---:|---:|---:|---:|
| 24.2.7.2 header | 21.587 | 13.910 | 17.995 | 755.940 |
| 24.2.7.2 footer | 584.586 | 5.740 | 17.995 | 755.940 |
| 26.2.4.2 header | 21.607 | 13.889 | 17.996 | 755.897 |
| 26.2.4.2 footer | 584.585 | 5.741 | 17.996 | 755.940 |
| ours, after | 21.600 | 13.932 | 18.000 | 756.000 |

**The rectangle is as tall as the band at the print scale and as wide as the band at full size.**
The header band the margins imply is `0.75 − 0.3 = 0.45 in = 32.4 pt`, and `32.4 × 0.4293 = 13.91`;
its width is `774 − 18 = 756` with no scale at all. That asymmetry is the one
`SheetPrintSetup.PrintableAreaAt` already carries and cites: `aPageRect` is in *document* twips,
so `nLineWidth` comes off it and arrives unscaled while `nHeight − nDistance` is added whole and
arrives at `nHeight × zoom` (`ScPrintFunc::GetDocPageSize`, `printfun.cxx:3002`, over the map
mode's zoom fraction at `:2645`). The footer confirms the same arithmetic from the other end: its
band is dynamic, so `nHeight − nDistance` is its own one-line text height of 13.4 pt, and
`13.4 × 0.4293 = 5.75`.

We emit no footer clip on this document because our footer's ink is wholly inside its rectangle;
the reference emits one and draws the same thing.

## Consequence on the page

The header's third line, `As of 10/20/2023`, does not fit a band that holds two. Both engines
draw it. The reference cuts it at 35.50 pt — `pdftotext` still reads all three lines off its page
— and before this round we drew the whole line over the sheet's own header row.

## Reach

Measured by rendering the 171 sheets documents that carry a header or footer, or that are not a
zip (so `.xls` and `.ods` are all included), through the binary either side of the change, and
comparing the PDFs byte for byte: **22 of 171 moved**. Scored against both references over up to
six pages each, mean ink at 60 dpi grayscale:

| | 24.2 before | 24.2 after | 26.2 before | 26.2 after |
|---|---:|---:|---:|---:|
| `020_Free_Blood_Pressure_Chart…` | 0.769 | **0.612** | 2.704 | **2.516** |
| `CSA_CCM_v1.2.xls` | 2.848 | **2.701** | 2.161 | **2.014** |
| `TICAPCapability_Final.xls` | 9.420 | **9.361** | 9.421 | **9.363** |
| `Hazard Analysis Template.xls` | 7.107 | **7.056** | 7.564 | **7.512** |
| `RMP 2011-2014 and Inventory.xls` | 3.675 | **3.655** | 3.471 | **3.451** |
| `orbus_togaf_tool_csq.xls` | 4.845 | **4.827** | 4.372 | **4.353** |
| `FY2023-AIP-grants.xlsx` | 13.484 | **13.477** | 13.191 | **13.184** |
| 12 others | | ≤0.01 better | | ≤0.01 better |
| `066_Agile_Gantt_chart` | 4.092 | 4.093 | 3.911 | 3.912 |
| `027_Simple_personal_cash_flow_statement` | 3.535 | 3.537 | 6.372 | 6.373 |

Nineteen improved, one unchanged, and two moved the wrong way by 0.001–0.002 — a thousandth of a
grey level, which is a rounding of the clip edge and not a visible change. Nothing in the set
lost a word: the clip is `ClipPathKeepingText`, so the glyphs stay in the PDF's text layer
exactly as the reference's do.

The change cannot reach a document with no header or footer, and at an unscaled sheet the new
rectangle is the same figure as the old one, so only a *scaled* sheet whose band overflows can
move at all.
