// Tests for the Studio page's pure helpers (run with: node tests/studio.test.mjs).
// The page is a browser script, so the functions under test are lifted out of it by name.
import { readFileSync } from "node:fs";
import assert from "node:assert/strict";

const src = readFileSync(new URL("../Studio/web/studio.js", import.meta.url), "utf8");

function lift(name) {
  const start = src.indexOf(`function ${name}(`);
  assert.ok(start >= 0, `${name} not found in studio.js`);
  // The function ends at the first line that is just "}" after it starts.
  const end = src.indexOf("\n}", start);
  return new Function(`${src.slice(start, end + 2)}; return ${name};`)();
}

const parseIcs = lift("parseIcs");
let passed = 0;
function test(name, fn) {
  try { fn(); passed++; console.log(`PASS  ${name}`); }
  catch (e) { console.log(`FAIL  ${name}\n      ${e.message}`); process.exitCode = 1; }
}

const now = new Date(2026, 9, 2, 8, 0, 0);   // Fri 2 Oct 2026, 08:00 local
const cal = (...events) => ["BEGIN:VCALENDAR", ...events.flatMap(e => ["BEGIN:VEVENT", ...e, "END:VEVENT"]), "END:VCALENDAR"].join("\r\n");

test("a timed event is reminded ten minutes before", () => {
  const { events } = parseIcs(cal(["SUMMARY:Dentist", "DTSTART:20261002T140000"]), now);
  assert.deepEqual(events, [{ text: "Dentist at 14:00", when: "2026-10-02T13:50" }]);
});

test("quoted time-zone parameters (with colons) are understood", () => {
  const { events } = parseIcs(cal(["SUMMARY:Call", 'DTSTART;TZID="(UTC+01:00) Amsterdam, Berlin":20261003T100000']), now);
  assert.equal(events.length, 1);
  assert.equal(events[0].text, "Call at 10:00");
});

test("all-day events say so, and count for the whole day", () => {
  const today = parseIcs(cal(["SUMMARY:Mum's birthday", "DTSTART;VALUE=DATE:20261002"]), new Date(2026, 9, 2, 12, 0, 0)).events;
  assert.equal(today.length, 1);
  assert.equal(today[0].text, "Mum's birthday (today)");
});

test("weekly repeats come back to the next one", () => {
  const { events } = parseIcs(cal(["SUMMARY:Standup", "DTSTART:20250106T093000", "RRULE:FREQ=WEEKLY"]), now);
  assert.equal(events.length, 1);
  assert.equal(events[0].when, "2026-10-05T09:20");   // the next Monday
});

test("repeats that have ended, past events and far-off events are left out", () => {
  const { events } = parseIcs(cal(
    ["SUMMARY:Old", "DTSTART:20240101T100000"],
    ["SUMMARY:Ended", "DTSTART:20250101T100000", "RRULE:FREQ=DAILY;UNTIL=20250201T000000Z"],
    ["SUMMARY:Next year", "DTSTART:20271001T100000"]), now);
  assert.deepEqual(events, []);
});

test("unusual repeat patterns are counted as skipped", () => {
  const { events, skipped } = parseIcs(cal(["SUMMARY:Hourly", "DTSTART:20250101T100000", "RRULE:FREQ=HOURLY"]), now);
  assert.equal(events.length, 0);
  assert.equal(skipped, 1);
});

test("escaped text and folded lines are tidied", () => {
  const ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Lunch\\, then\\; a very long\r\n  title\\nhere\r\nDTSTART:20261002T120000\r\nEND:VEVENT\r\nEND:VCALENDAR";
  const { events } = parseIcs(ics, now);
  assert.equal(events[0].text, "Lunch, then; a very long title here at 12:00");
});

test("an event starting very soon is reminded straight away", () => {
  const { events } = parseIcs(cal(["SUMMARY:Soon", "DTSTART:20261002T080500"]), now);
  assert.equal(events[0].when, "2026-10-02T08:01");
});

console.log(process.exitCode ? "SOME STUDIO TESTS FAILED" : `ALL ${passed} STUDIO TESTS PASSED`);
