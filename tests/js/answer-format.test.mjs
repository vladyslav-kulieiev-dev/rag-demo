// Run with: node --test "tests/js/*.test.mjs"
import { test } from "node:test";
import assert from "node:assert/strict";
import { parseAnswer, parseInline, marker } from "../../src/RagDemo.Api/wwwroot/answer-format.mjs";

test("bold, italic and plain text", () => {
  assert.deepEqual(parseInline("a **b** *c* d"), [
    { text: "a " }, { text: "b", bold: true }, { text: " " }, { text: "c", italic: true }, { text: " d" }
  ]);
});

test("markers are found inside and outside bold text", () => {
  assert.deepEqual(parseInline(`**z upływem miesiąca${marker(0)}** pracy.${marker(1)}`), [
    { text: "z upływem miesiąca", bold: true }, { marker: 0 }, { text: " pracy." }, { marker: 1 }
  ]);
});

test("stars that aren't formatting stay as text", () => {
  assert.deepEqual(parseInline("2 * 3 = 6 and a**b"), [{ text: "2 * 3 = 6 and a**b" }]);
  assert.deepEqual(parseInline("unclosed **bold"), [{ text: "unclosed **bold" }]);
});

test("the answer from the screenshot becomes a list with a bold lead", () => {
  const blocks = parseAnswer(
    "Prawo do urlopu:\n\n" +
    `* **Nabywanie prawa do urlopu z upływem miesiąca:** Pracownik uzyskuje prawo do urlopu.${marker(0)}\n` +
    `* **Kolejne lata:** Prawo do kolejnych urlopów nabywa co roku.${marker(1)}`);

  assert.equal(blocks.length, 2);
  assert.deepEqual(blocks[0], { type: "p", inline: [{ text: "Prawo do urlopu:" }] });
  assert.equal(blocks[1].type, "ul");
  assert.equal(blocks[1].items.length, 2);
  assert.deepEqual(blocks[1].items[0], [
    { text: "Nabywanie prawa do urlopu z upływem miesiąca:", bold: true },
    { text: " Pracownik uzyskuje prawo do urlopu." },
    { marker: 0 }
  ]);
});

test("numbered lists, headings, and lines joined into one paragraph", () => {
  const blocks = parseAnswer("## Wymiar\n1. 20 dni\n2) 26 dni\nFirst line\nsecond line");
  assert.deepEqual(blocks.map(b => b.type), ["heading", "ol", "p"]);
  assert.equal(blocks[1].items.length, 2);
  assert.deepEqual(blocks[2].inline, [{ text: "First line" }, { text: " " }, { text: "second line" }]);
});
