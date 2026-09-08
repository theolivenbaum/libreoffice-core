using Paperless.MsBinary.Records;

namespace Paperless.Presentations.MsBinary;

/// <summary>
/// The identifiers of the hyperlinks a deck declares, which is what decides whether a text
/// range's <c>InteractiveInfo</c> becomes a field.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A <c>TxInteractiveInfoAtom</c> is not by itself a hyperlink.</strong>
/// <c>PPTTextObj</c>'s <c>PPT_PST_InteractiveInfo</c> case searches
/// <c>SdrPowerPointImport::m_aHyperList</c> for an entry whose index matches the
/// <c>InteractiveInfoAtom</c>'s <c>exHyperlinkId</c>, and everything that makes the run a
/// field — the <c>SvxURLField</c>, the underline, the scheme colour — happens inside that
/// loop (<c>filter/source/msfilter/svdfppt.cxx</c>:6907-6941). With no matching entry the
/// record is stepped over and the text is drawn exactly as it stands. This is the legacy
/// twin of DrawingML's <em>the hyperlink property map must be non-empty</em>, and it is not
/// a formality: <c>BUS-Chapter 05.ppt</c> states <strong>57</strong> live text ranges and
/// declares no hyperlink at all, and 26.2.4.2 draws every one of them in the body's own
/// colour, word-broken and unlinked.
/// </para>
/// <para>
/// <strong>The list is filled from the document summary and <em>indexed</em> from here.</strong>
/// <c>ImplSdPPTImport::Import</c> builds one entry per link of the
/// <c>_PID_HLINKS</c> blob in the user-defined property section
/// (<c>sd/source/filter/ppt/pptin.cxx</c>:353-518, six properties per link) and then gives the
/// <em>k</em>th entry the <em>k</em>th <c>ExHyperlinkAtom</c> of the <c>ExObjList</c>
/// (<c>:531-547</c>); when the blob is absent it builds an entry per <c>ExHyperlink</c>
/// instead (<c>:551-575</c>). Either way <strong>the set of indices that can match is the set
/// of <c>ExHyperlinkAtom</c> values</strong>, so this reads those and does not touch the
/// property set — measured over the corpus's 51 <c>.ppt</c>, where the <c>_PID_HLINKS</c>
/// count equals the live <c>ExHyperlinkAtom</c> count on all 27 documents that state either,
/// with no disagreement. See <c>probes/ppt-autofit-r84/results.md</c>.
/// </para>
/// <para>
/// <strong>It must be read off the <em>live</em> document container.</strong> A <c>.ppt</c>
/// stream keeps every superseded version of every object, and a scan of the stream finds
/// orphaned <c>ExObjList</c> records as readily as the current one — on
/// <c>080214-Intl-pol-frameworks…ppt</c> a stream scan finds a dead list declaring one link
/// where the live one declares two, which would have left the second URL unlinked. The
/// container this is given comes from the persist directory, so the question does not arise.
/// </para>
/// </remarks>
internal static class PptHyperlinks
{
    /// <summary>
    /// The <c>ExHyperlinkAtom</c> values the document's <c>ExObjList</c> declares.
    /// </summary>
    /// <remarks>
    /// Every value is kept, duplicates included, because the count is what bounds the entry
    /// list; membership is all a text range asks of it.
    /// </remarks>
    /// <param name="stream">The <c>PowerPoint Document</c> stream.</param>
    /// <param name="document">The live <c>Document</c> container.</param>
    public static HashSet<uint> Read(DffRecordBuffer stream, DffRecordHeader document)
    {
        ArgumentNullException.ThrowIfNull(stream);

        HashSet<uint> ids = [];

        if (stream.FirstChild(document, PptRecordTypes.ExObjList) is not { } list) return ids;

        foreach (DffRecordHeader link in stream.Children(list))
        {
            if (link.Type != PptRecordTypes.ExHyperlink) continue;

            foreach (DffRecordHeader atom in stream.Children(link))
            {
                if (atom.Type != PptRecordTypes.ExHyperlinkAtom) continue;

                ReadOnlySpan<byte> content = stream.Content(atom);
                if (content.Length >= 4) ids.Add(DffRecordBuffer.ReadUInt32(content));
                break;
            }
        }

        return ids;
    }
}
