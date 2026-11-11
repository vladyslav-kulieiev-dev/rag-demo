// Finds where a cited chunk sits in a document's text, so the whole chunk can be highlighted.
//
// The chunk comes from the layout parser and the document text from the PDF, and they never match
// exactly: the chunk has markdown and headings, the PDF has page headers, page numbers and words
// hyphenated across lines. So the two texts are compared only by letters and digits, and walked
// side by side. When a character doesn't match, the next few chunk characters are looked for a
// little further on in the document (skipping a page header, say). When they aren't there, the
// chunk character is skipped instead (a markdown heading the PDF doesn't have, say).

const DEFAULTS = {
  anchorLength: 24, // characters used to find where the chunk starts
  maxAnchors: 10,   // how many places the start is tried at (a table of contents can repeat it)
  groupLength: 8,   // characters that must match to pick up again after a difference
  window: 100,      // how far ahead in the document to look for them
  maxSkip: 300,     // stop after this many chunk characters in a row that can't be found
  minRunLength: 15  // shorter matched pieces are dropped: they'd be highlighted all over the document
};

/**
 * @param {string[]} pages Text of each page, built like PDF.js does: item strings, "\n" after line ends.
 * @param {string} chunk The cited chunk text.
 * @returns {{ page: number, text: string }[]} Pieces of the page text (page is 0-based) that make up the chunk.
 */
export function alignChunk(pages, chunk, options = {}) {
  const o = { ...DEFAULTS, ...options };
  const doc = comparable(pages.join("\n"));
  const target = comparable(chunk);
  if (target.text.length === 0 || doc.text.length === 0) return [];

  // Try each place the chunk could start, and keep the one that lines up best: most characters
  // matched, minus document characters jumped over. Without the penalty, a start in the table of
  // contents could win by jumping ahead into the body.
  let best = [];
  let bestScore = 0;
  for (const [chunkStart, docStart] of anchors(doc.text, target.text, o)) {
    const { matched, jumped } = walk(doc.text, target.text, chunkStart, docStart, o);
    const score = matched.length - jumped;
    if (score > bestScore) {
      best = matched;
      bestScore = score;
    }
  }
  if (best.length === 0) return [];

  return toPieces(best, doc.map, pages, o.minRunLength);
}

/** Lowercase letters and digits only, with each one's position in the original text. */
export function comparable(text) {
  const chars = [];
  const map = [];
  for (let i = 0; i < text.length; i++) {
    const c = text[i].toLowerCase();
    if (c.length === 1 && /[\p{L}\p{N}]/u.test(c)) {
      chars.push(c);
      map.push(i);
    }
  }
  return { text: chars.join(""), map };
}

// Start positions: the first piece of the chunk that occurs in the document, at every place it occurs.
function anchors(doc, chunk, o) {
  const length = Math.min(o.anchorLength, chunk.length);
  for (let start = 0; start + length <= chunk.length; start += Math.max(1, length >> 1)) {
    const piece = chunk.substr(start, length);
    const found = [];
    for (let at = doc.indexOf(piece); at >= 0 && found.length < o.maxAnchors; at = doc.indexOf(piece, at + 1)) {
      found.push([start, at]);
    }
    if (found.length > 0) return found;
  }
  return [];
}

// Walks both texts from a start point. Returns the document positions that matched,
// and how many document characters were jumped over.
function walk(doc, chunk, c, d, o) {
  const matched = [];
  let skipped = 0;
  let jumped = 0;
  while (c < chunk.length && d < doc.length) {
    if (chunk[c] === doc[d]) {
      matched.push(d);
      c++;
      d++;
      skipped = 0;
      continue;
    }

    // Different here. Is the next group of chunk characters a little further on in the document?
    const group = chunk.substr(c, o.groupLength);
    const ahead = group.length >= Math.min(4, o.groupLength)
      ? doc.slice(d, d + o.window + group.length).indexOf(group)
      : -1;
    if (ahead > 0) {
      d += ahead; // the document has extra text here: skip it
      jumped += ahead;
      continue;
    }

    c++; // the chunk has text the document doesn't: skip one chunk character
    if (++skipped > o.maxSkip) break;
  }
  return { matched, jumped };
}

// Turns matched positions into pieces of the original page text, split at gaps and page ends.
function toPieces(matched, map, pages, minRunLength) {
  const pageStarts = [];
  let offset = 0;
  for (const page of pages) {
    pageStarts.push(offset);
    offset += page.length + 1; // the "\n" pages were joined with
  }
  const pageOf = position => {
    let p = 0;
    while (p + 1 < pageStarts.length && pageStarts[p + 1] <= position) p++;
    return p;
  };

  const pieces = [];
  let runStart = 0;
  for (let i = 1; i <= matched.length; i++) {
    const continues = i < matched.length
      && matched[i] === matched[i - 1] + 1
      && pageOf(map[matched[i]]) === pageOf(map[matched[runStart]]);
    if (continues) continue;

    if (i - runStart >= minRunLength) {
      const from = map[matched[runStart]];
      const to = map[matched[i - 1]] + 1;
      const page = pageOf(from);
      pieces.push({ page, text: pages[page].slice(from - pageStarts[page], to - pageStarts[page]) });
    }
    runStart = i;
  }
  return pieces;
}
