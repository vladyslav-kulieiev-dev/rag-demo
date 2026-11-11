// Run with: node --test "tests/js/*.test.mjs"
import { test } from "node:test";
import assert from "node:assert/strict";
import { alignChunk, comparable } from "../../src/RagDemo.Api/wwwroot/chunk-align.mjs";

// The chunk as the layout parser returns it (copied from a real Answer API response).
const chunk = "## Urlopy wypoczynkowe Art. 152. § 1. Pracownikowi przysługuje prawo do corocznego, nieprzerwanego, " +
  "płatnego urlopu wypoczynkowego, zwanego dalej „urlopem\". § 2. Pracownik nie może zrzec się prawa do urlopu. " +
  "Art. 153. § 1. Pracownik podejmujący pracę po raz pierwszy, w roku kalendarzowym, w którym podjął pracę, " +
  "uzyskuje prawo do urlopu z upływem każdego miesiąca pracy, w wymiarze 1/12 wymiaru urlopu przysługującego " +
  "mu po przepraco- waniu roku.";

// The same text as PDF.js extracts it: a table of contents, line breaks, a hyphenated word,
// typographic quotes, and a page header in the middle of the chunk.
const pages = [
  "Spis treści\nDział siódmy Urlopy pracownicze\nUrlopy wypoczynkowe Art. 152 ... 45\n",
  "Dział siódmy\nUrlopy pracownicze\nRozdział I\nUrlopy wypoczynkowe\nArt. 152. § 1. Pracownikowi przysługuje prawo do coroczne-\n" +
    "go, nieprzerwanego, płatnego urlopu wypoczynkowego, zwanego dalej „urlopem”.\n§ 2. Pracownik nie może zrzec się prawa do urlopu.\n" +
    "Art. 153. § 1. Pracownik podejmujący pracę po raz pierwszy, w roku kalendarzowym, w którym\n",
  "Dziennik Ustaw – 46 – Poz. 277\npodjął pracę, uzyskuje prawo do urlopu z upływem każdego miesiąca pracy, w wymiarze 1/12\n" +
    "wymiaru urlopu przysługującego mu po przepraco-\nwaniu roku.\n§ 2. Prawo do kolejnych urlopów pracownik nabywa w każdym następnym roku.\n"
];

const joined = pieces => pieces.map(p => p.text).join(" ");

test("comparable keeps only lowercase letters and digits, with positions", () => {
  const { text, map } = comparable("Art. 15, Ż!");
  assert.equal(text, "art15ż");
  assert.deepEqual(map, [0, 1, 2, 5, 6, 9]);
});

test("finds the chunk in the body, not in the table of contents", () => {
  const pieces = alignChunk(pages, chunk);
  assert.ok(pieces.length > 0);
  assert.ok(pieces.every(p => p.page >= 1), JSON.stringify(pieces));
});

test("covers the chunk from Art. 152 to the end of Art. 153 § 1", () => {
  const text = joined(alignChunk(pages, chunk));
  assert.match(text, /Art\. 152\. § 1\. Pracownikowi/);
  assert.match(text, /zrzec się prawa do urlopu/);
  assert.match(text, /podjął pracę, uzyskuje prawo/);
  assert.match(text, /przepraco-\nwaniu roku/);
});

test("skips text that is only in the PDF (page header) or only in the chunk", () => {
  const text = joined(alignChunk(pages, chunk));
  assert.doesNotMatch(text, /Dziennik Ustaw/);
  assert.doesNotMatch(text, /Prawo do kolejnych/); // after the chunk ends
});

test("pieces are exact substrings of their page, split at page ends", () => {
  const pieces = alignChunk(pages, chunk);
  for (const piece of pieces) assert.ok(pages[piece.page].includes(piece.text), piece.text);
  assert.ok(pieces.some(p => p.page === 1) && pieces.some(p => p.page === 2));
});

test("returns nothing when the chunk isn't in the document", () => {
  assert.deepEqual(alignChunk(pages, "The boiling point of mercury is 357 degrees Celsius."), []);
  assert.deepEqual(alignChunk(pages, ""), []);
});
