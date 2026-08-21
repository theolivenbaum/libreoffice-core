#!/usr/bin/env python3
"""24.2.7-audit: do ODF's two paragraph-spacing settings still behave as the site records?

    python3 audit_odfspacing.py <outdir> [workers]

`OdtLayoutSource.AddsParagraphSpacing` and `OdtLayoutSource.KeepsParagraphSpacingAtPages` each carry
a measurement taken on **24.2.7.2** and the reference binary is now 26.2.4.2. Round 59 re-checked
the DOCX twin of the first (`WordCompatibility.AddsParagraphSpacing`) and verified it; this is the
ODF pair, which is a different code path in LibreOffice — `SwXDocumentSettings` reading
`office:settings` rather than `DomainMapper_Impl::ApplySettingsTable` reading `w:compat`.

The claims, quoted from the sites:

  * `AddParaTableSpacing` **false** puts every paragraph boundary at **24.00 pt** (the larger of a
    12 pt space-before and an 8 pt space-after wins) and **true** at **32.00 pt** (they sum).
    Absent means **true**, because the application default is `true`. The name says the opposite of
    what it does, which is the reason it is worth a probe at all.
  * `AddParaTableSpacingAtStart` **true** puts the first baseline at **93.60 pt** down an A4 page,
    **false** at **81.60**, and **absent** at 93.60.

Six arms, and the two `absent` arms are the discriminators — a claim that a *stated* value is
honoured says nothing about which way an unstated one falls, and it is the unstated case that every
real document takes.

Measured by the reference's own PDF geometry: the boundary is the difference between two consecutive
first-baselines, and the top is the first baseline against the page's top edge. Both are read off
text origins, so nothing here depends on reading a border or a fill.

Flat ODF rather than a zipped `.odt`, because the whole document is one file and the arms differ by
one element — there is no chance of an arm silently carrying the wrong settings part.
"""
import os
import re
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

PAGE_H = 841.89
PARAGRAPHS = 8

NS = ' '.join([
    'xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"',
    'xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"',
    'xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"',
    'xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"',
    'xmlns:config="urn:oasis:names:tc:opendocument:xmlns:config:1.0"',
])


def document(setting):
    """`setting` is None for no `office:settings` at all, else (name, 'true'|'false')."""
    if setting is None:
        settings = ''
    else:
        name, value = setting
        settings = (
            '<office:settings><config:config-item-set '
            'config:name="ooo:configuration-settings">'
            f'<config:config-item config:name="{name}" config:type="boolean">{value}'
            '</config:config-item></config:config-item-set></office:settings>')

    body = ''.join(f'<text:p text:style-name="P1">Line {i}</text:p>'
                   for i in range(PARAGRAPHS))

    return (
        '<?xml version="1.0" encoding="UTF-8"?>'
        f'<office:document {NS} office:version="1.3" '
        'office:mimetype="application/vnd.oasis.opendocument.text">'
        + settings +
        '<office:automatic-styles>'
        '<style:style style:name="P1" style:family="paragraph">'
        '<style:paragraph-properties fo:margin-top="12pt" fo:margin-bottom="8pt" '
        'fo:line-height="12pt"/>'
        '<style:text-properties style:font-name="Liberation Serif" fo:font-size="12pt"/>'
        '</style:style>'
        '<style:page-layout style:name="PL1"><style:page-layout-properties '
        'fo:page-width="21cm" fo:page-height="29.7cm" fo:margin-top="2.54cm" '
        'fo:margin-bottom="2.54cm" fo:margin-left="2.54cm" fo:margin-right="2.54cm"/>'
        '</style:page-layout>'
        '</office:automatic-styles>'
        '<office:master-styles><style:master-page style:name="Standard" '
        'style:page-layout-name="PL1"/></office:master-styles>'
        f'<office:body><office:text>{body}</office:text></office:body>'
        '</office:document>')


CASES = [
    ('add-absent', None,
     'no office:settings at all — expect ADDED, 32.00 pt boundaries and a 93.60 pt first baseline'),
    ('add-false', ('AddParaTableSpacing', 'false'),
     'expect COLLAPSED, 24.00 pt boundaries'),
    ('add-true', ('AddParaTableSpacing', 'true'),
     'expect ADDED, 32.00 pt boundaries'),
    ('atstart-absent', None,
     'the same document again, read for its first baseline — expect 93.60'),
    ('atstart-false', ('AddParaTableSpacingAtStart', 'false'),
     'expect the first baseline at 81.60, the space dropped'),
    ('atstart-true', ('AddParaTableSpacingAtStart', 'true'),
     'expect the first baseline at 93.60'),
]


def render_ref(src, outdir, slot):
    subprocess.run(
        ['soffice', '-env:UserInstallation=file://' + os.path.join(outdir, f'prof{slot}'),
         '--headless', '--norestore', '--convert-to', 'pdf',
         '--outdir', os.path.join(outdir, 'ref'), src], capture_output=True, timeout=300)


def render_ours(cli, src, outdir):
    subprocess.run([cli, 'render', src, '--outdir', os.path.join(outdir, 'ours')],
                   capture_output=True, timeout=300)


sys.path.insert(0, "/c/sandbox/workdir/wt-words-r50/dotnet/research/probes/slides-r15")


def baselines(pdf):
    """Every text origin's y on page 1, top-down, in drawing order."""
    from pdfops import objects, pages, content
    data = open(pdf, 'rb').read()
    objs = objects(data)
    out = []
    for pn in pages(data, objs):
        c = content(data, objs, pn).decode('latin1')
        cur = None
        for m in re.finditer(r'([-\d.]+) ([-\d.]+) Td|(?:TJ|Tj)', c):
            if m.group(2):
                cur = float(m.group(2))
            elif cur is not None:
                out.append(round(PAGE_H - cur, 2))
        break
    return out


if __name__ == '__main__':
    out = os.path.abspath(sys.argv[1])
    workers = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    cli = os.environ.get('PAPERLESS_CLI') or (
        '/c/sandbox/workdir/wt-words-r50/dotnet/tools/Paperless.Cli/bin/Debug/net10.0/'
        'linux-x64/Paperless.Cli')
    if os.path.isdir(out):
        shutil.rmtree(out)
    for d in ('in', 'ref', 'ours'):
        os.makedirs(os.path.join(out, d), exist_ok=True)

    built = []
    for i, (name, setting, why) in enumerate(CASES):
        stem = '%02d-%s' % (i, name)
        path = os.path.join(out, 'in', stem + '.fodt')
        with open(path, 'w', encoding='utf-8') as f:
            f.write(document(setting))
        built.append((name, stem, path, why))

    with ThreadPoolExecutor(workers) as pool:
        list(pool.map(lambda t: render_ref(t[2], out, built.index(t) % workers), built))
    for t in built:
        render_ours(cli, t[2], out)

    missing = ['%s: no %s' % (t[1], s) for t in built for s in ('ref', 'ours')
               if not os.path.exists(os.path.join(out, s, t[1] + '.pdf'))]
    if missing:
        print('REFUSING TO PRINT — %d halves missing: %s' % (len(missing), missing))
        sys.exit(2)

    read = {}
    for name, stem, path, why in built:
        for side in ('ref', 'ours'):
            ys = baselines(os.path.join(out, side, stem + '.pdf'))
            if len(ys) != PARAGRAPHS:
                print('REFUSING TO PRINT — %s/%s drew %d baselines, not %d'
                      % (side, stem, len(ys), PARAGRAPHS))
                sys.exit(2)
            read[(stem, side)] = ys

    print('%d flat-ODF packages, %d halves, every half drew all %d baselines\n'
          % (len(built), 2 * len(built), PARAGRAPHS))
    print('%-16s %-22s %-22s %s' % ('case', 'reference top / pitch', 'ours', 'what it tests'))
    for name, stem, path, why in built:
        r = read[(stem, 'ref')]
        o = read[(stem, 'ours')]
        rp = sorted({round(b - a, 2) for a, b in zip(r, r[1:])})
        op = sorted({round(b - a, 2) for a, b in zip(o, o[1:])})
        agree = 'AGREE' if (r[0], rp) == (o[0], op) else 'DIFFER'
        print('%-16s %7.2f / %-12s %7.2f / %-12s %-7s %s'
              % (name, r[0], rp, o[0], op, agree, why))
