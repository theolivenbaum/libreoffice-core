#!/usr/bin/env python3
"""Is a table of contents wrapped beside a floating obstacle, or does it descend past it?

`absrc-pac-01-info-note-en.doc` page 1 holds a left-anchored one-column table with 319 pt of
room to its right. 26.2.4.2 wraps an ordinary paragraph beside it — both engines agree on that
— and puts the `text:table-of-content` that follows **below** it at the left margin, where this
tree wraps the index's paragraphs beside it too, 179.9 pt right and 340.9 pt high.
`probes/pages-r164` §3 proposed that a Writer section frame is not wrapped beside a fly and
left it unsettled.

These arms settle it in DOCX, where the question can be asked cheaply: the hypothesis is about
Writer's layout rather than about the WW8 reader, so if it reproduces here the seat is
`FrameLayout` and if it does not the seat is the reader.

`word/settings.xml` is written even though it is empty (`paperless-corpus/SKILL.md`).
"""
import os, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "fixtures")
os.makedirs(OUT, exist_ok=True)

NS = ('xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" '
      'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"')

CT = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
<Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
</Types>'''
RELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'''
DRELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/></Relationships>'''
SETTINGS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>'''
STYLES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Liberation Serif" w:hAnsi="Liberation Serif"/><w:sz w:val="24"/></w:rPr></w:rPrDefault><w:pPrDefault/></w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
<w:style w:type="paragraph" w:styleId="TOC1"><w:name w:val="toc 1"/></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/>
  <w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
</w:styles>'''

# A floating table: one column 2.1417 in wide, anchored left, which is `absrc`'s `Table2`.
FLOATER = '''<w:tbl>
  <w:tblPr>
    <w:tblpPr w:leftFromText="180" w:rightFromText="180" w:vertAnchor="text"
              w:horzAnchor="margin" w:tblpX="1" w:tblpY="1"/>
    <w:tblW w:w="3084" w:type="dxa"/>
    <w:tblBorders><w:top w:val="single" w:sz="4"/><w:left w:val="single" w:sz="4"/>
      <w:bottom w:val="single" w:sz="4"/><w:right w:val="single" w:sz="4"/></w:tblBorders>
  </w:tblPr>
  <w:tblGrid><w:gridCol w:w="3084"/></w:tblGrid>
  %s
</w:tbl>'''
ROW = ('<w:tr><w:tc><w:tcPr><w:tcW w:w="3084" w:type="dxa"/></w:tcPr>'
       '<w:p><w:r><w:t>QUICK LINK %d</w:t></w:r></w:p></w:tc></w:tr>')


def prose(n, tag="PROSE"):
    return "".join(
        f'<w:p><w:r><w:t>{tag} line {i} beside or below the quick links table.</w:t></w:r></w:p>'
        for i in range(1, n + 1))


def toc(entries):
    """A `TOC` field whose cached result is a run of `toc 1` paragraphs, as Word writes one."""
    body = "".join(
        f'<w:p><w:pPr><w:pStyle w:val="TOC1"/></w:pPr>'
        f'<w:r><w:t>INDEX ENTRY {i}</w:t></w:r></w:p>'
        for i in range(1, entries + 1))
    return (
        '<w:p><w:pPr><w:pStyle w:val="TOC1"/></w:pPr>'
        '<w:r><w:fldChar w:fldCharType="begin"/></w:r>'
        '<w:r><w:instrText xml:space="preserve"> TOC \\o "1-3" \\h \\z \\u </w:instrText></w:r>'
        '<w:r><w:fldChar w:fldCharType="separate"/></w:r>'
        '<w:r><w:t>INDEX ENTRY 0</w:t></w:r></w:p>'
        + body
        + '<w:p><w:pPr><w:pStyle w:val="TOC1"/></w:pPr>'
          '<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>')


def arms():
    rows = "".join(ROW % i for i in range(1, 8))
    table = FLOATER % rows

    # The control both engines already agree on: ordinary paragraphs beside the obstacle.
    yield "plain-after", table + prose(10)

    # The witness: a table of contents where those paragraphs were.
    yield "toc-after", table + toc(9)

    # A heading before the index, as `absrc` has (`INFORMATION HIGHLIGHTS`), so the arm shows
    # whether the heading wraps while the index does not.
    yield "heading-then-toc", (
        table
        + '<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr>'
          '<w:r><w:t>INFORMATION HIGHLIGHTS</w:t></w:r></w:p>'
        + toc(9))

    # And the same heading before plain prose, as the control for that.
    yield "heading-then-plain", (
        table
        + '<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr>'
          '<w:r><w:t>INFORMATION HIGHLIGHTS</w:t></w:r></w:p>'
        + prose(10))


def build(name, body):
    doc = (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document {NS}><w:body>'
           + body +
           '<w:sectPr><w:pgSz w:w="12240" w:h="15840"/>'
           '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"'
           ' w:header="720" w:footer="720"/></w:sectPr></w:body></w:document>')
    path = os.path.join(OUT, f"{name}.docx")
    if os.path.exists(path):
        os.remove(path)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", CT)
        z.writestr("_rels/.rels", RELS)
        z.writestr("word/_rels/document.xml.rels", DRELS)
        z.writestr("word/settings.xml", SETTINGS)
        z.writestr("word/styles.xml", STYLES)
        z.writestr("word/document.xml", doc)


if __name__ == "__main__":
    n = 0
    for name, body in arms():
        build(name, body)
        n += 1
    print("built", n, "fixtures in", OUT)
