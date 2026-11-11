// Parses the small part of Markdown the Answer API uses (paragraphs, bullet and numbered lists,
// headings, **bold**, *italic*) into plain data. The page turns it into DOM nodes, so answer text
// is never inserted as HTML.
//
// Citation markers are passed in as placeholders: MARKER_START + index + MARKER_END.

export const MARKER_START = "";
export const MARKER_END = "";

export const marker = index => `${MARKER_START}${index}${MARKER_END}`;

/**
 * @returns {Array<{type: "p" | "heading", inline: Inline[]} | {type: "ul" | "ol", items: Inline[][]}>}
 *   where Inline is {text, bold, italic} or {marker: number}.
 */
export function parseAnswer(text) {
  const blocks = [];
  let list = null;
  let paragraph = null;

  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) {
      list = null;
      paragraph = null;
      continue;
    }

    const heading = /^#{1,6}\s+(.*)$/.exec(line);
    const bullet = /^[*\-+•]\s+(.*)$/.exec(line);
    const numbered = /^\d+[.)]\s+(.*)$/.exec(line);

    if (heading) {
      blocks.push({ type: "heading", inline: parseInline(heading[1]) });
      list = paragraph = null;
    } else if (bullet || numbered) {
      const type = bullet ? "ul" : "ol";
      if (!list || list.type !== type) {
        list = { type, items: [] };
        blocks.push(list);
      }
      list.items.push(parseInline((bullet ?? numbered)[1]));
      paragraph = null;
    } else if (list && /^\s{2,}/.test(raw)) {
      // An indented line continues the last list item.
      list.items.at(-1).push({ text: " " }, ...parseInline(line));
    } else if (paragraph) {
      paragraph.inline.push({ text: " " }, ...parseInline(line));
    } else {
      paragraph = { type: "p", inline: parseInline(line) };
      blocks.push(paragraph);
      list = null;
    }
  }
  return blocks;
}

// Markers, **bold** and *italic*. Bold and italic text can contain markers and each other.
const INLINE = new RegExp(
  `${MARKER_START}(\\d+)${MARKER_END}` +  // 1: marker
  `|\\*\\*(.+?)\\*\\*` +                  // 2: bold
  `|(?<![\\p{L}\\p{N}*])\\*(?!\\s)(.+?)(?<!\\s)\\*(?![\\p{L}\\p{N}*])`, // 3: italic, not a * inside a word
  "gu");

export function parseInline(text, style = {}) {
  const parts = [];
  let last = 0;
  for (const match of text.matchAll(INLINE)) {
    if (match.index > last) parts.push({ text: text.slice(last, match.index), ...style });
    if (match[1] !== undefined) parts.push({ marker: Number(match[1]) });
    else if (match[2] !== undefined) parts.push(...parseInline(match[2], { ...style, bold: true }));
    else parts.push(...parseInline(match[3], { ...style, italic: true }));
    last = match.index + match[0].length;
  }
  if (last < text.length) parts.push({ text: text.slice(last), ...style });
  return parts;
}
