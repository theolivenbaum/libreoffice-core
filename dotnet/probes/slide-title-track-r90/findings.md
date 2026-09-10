# A slide title is tracked out 18 % in the reference and not here

Found by looking at the page. `pdf-image-diff` on
`slides/chartset-008/pptx/038_Competitive_Advantage_Card_for_PowerPoint_and_Google_Slides`
flags the title band as *"marks displaced or reshaped"* on both pages; cropping it shows why.

![the title band, ours above and the reference below](title-pair.png)

## What is measured, and it is not what it first looks like

Both sides draw the same string in **the same face at the same size** — `LiberationSans-Bold`,
36.00 pt, per the PDF's own span data. **The glyph ink is identical to a hundredth of a point**:

    C 25.99 / 25.99    o 22.00 / 21.96    m 32.00 / 32.00
    e 20.02 / 20.02    t 11.99 / 11.99    i 10.01 / 9.97      (ours / reference)

So this is **not** a horizontal scale and not a different substitute face. What differs is the
*advance*. Over the identical prefix `Competitive Advantage Card –`:

| | width | vs the font's own metrics |
|---|---:|---|
| ours | **520.77 pt** | Liberation Sans Bold's natural advance for that string at 36 pt is **522.12 pt** — we are within 0.26 %, which is about what the title's `kern="1200"` would take off |
| 26.2.4.2 | **615.53 pt** | **+17.9 %**, an extra **3.34 pt per character** |

The reference tracks the title out; we draw it at the font's metrics. The wrap follows from that
and is correct on both sides given their own advances: the layout's title box is
`cx="8515350"` EMU = 670.5 pt, about 656 pt of text width after the default insets, so our
616.8 pt line still has room for `Slide` and the reference's 623.5 pt line does not.

## What it is not

- **Not `spc`.** The run is bare `<a:rPr lang="en-US" dirty="0"/>`; the layout's title placeholder
  states only `sz="3600"`; `p:titleStyle`'s `lvl1pPr/defRPr` states `sz`, `b`, `kern="1200"` and
  `<a:latin typeface="Helvetica"/>` and no spacing. The only `spc` anywhere in the deck is
  `spc="150"` on three master runs whose text is the `www.` footer, and `spc="0"` in
  `slideLayout3`, which this slide does not use.
- **Not proportional tracking.** The 9 pt body paragraphs on the same slide differ by only
  **+0.117 pt per character** (`competitive pricing and`, 107.0 against 109.7 pt). Four times the
  size carries twenty-eight times the extra advance, so whatever this is, it is not a per-em
  constant applied everywhere.
- **Not justification.** The master states `algn="l"`, and the reference's *second* line
  (`Slide Template`, the last line) is tracked out just as much — a justified paragraph leaves its
  last line alone.

## The lead I could not close

The title overflows its box vertically: two lines of 36 pt at the master's `lnSpc` of 90 % is
about 64.8 pt in a box `cy="739056"` EMU = 58.2 pt tall. The layout's `bodyPr` is
`<a:normAutofit/>` and the slide overrides it with `<a:noAutofit/>`. A fit-to-frame path that
stretches text horizontally when it does not fit is the obvious suspect and would explain why the
effect is confined to this one overflowing block — but it does **not** explain identical glyph ink,
because stretching scales letterforms and this does not. Something is adding advance without
touching the glyphs.

**Do not take the suspect as the cause.** It is where to point the instrument, not a diagnosis.

## What a round has to do

1. Name the mechanism in LibreOffice's own source and cite it. `SdrTextFitToSizeType`,
   `SvxCharScaleWidthItem`, `ImpEditEngine`'s stretch handling and Impress's autofit are the
   places to look; the discriminator is that any real candidate must add advance while leaving
   glyph advance widths untouched.
2. Establish which side is right for a *non*-overflowing title, so the fix does not track out
   every slide title in the corpus.
3. Census the reach. The signature is a slide text block whose content is taller than its box.
4. Score on geometry. The character count does not move — both sides draw the same characters —
   so no gate verdict will report this either way.
