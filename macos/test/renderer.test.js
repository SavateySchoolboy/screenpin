'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(__dirname, '..', 'renderer.js'), 'utf8');

function open(kind, data) {
  const dom = new JSDOM('<!doctype html><main id="app"></main>', { runScripts: 'outside-only', url: 'https://screenpin.invalid/' });
  let initial, locked; const calls = [];
  const record = key => (...args) => { calls.push([key, ...args]); return Promise.resolve(''); };
  dom.window.screenpin = { onInitial: fn => { initial = fn; }, onLocked: fn => { locked = fn; }, close: record('close'), lock: record('lock'), zoom: record('zoom'), opacity: record('opacity'), save: record('save'), copyImage: record('copyImage'), copyText: record('copyText'), dragImage: record('dragImage'), openLink: record('openLink'), binding: record('binding'), startup: record('startup'), settingsAction: record('settingsAction') };
  dom.window.eval(source); initial({ kind, data }); return { dom, calls, setLocked: value => locked(value) };
}
test('result shows selected browser and delegates copying and opening', async () => {
  const { dom, calls } = open('result', { value: 'https://example.org/path', browsers: [{ name: 'По умолчанию' }, { name: 'Safari' }] });
  assert.equal(dom.window.document.querySelector('.domain').textContent, 'example.org');
  const select = dom.window.document.querySelector('select'); select.value = '1';
  dom.window.document.querySelector('.actions button').click(); dom.window.document.querySelector('.actions button:last-child').click();
  assert.deepEqual(calls, [['copyText'], ['openLink', '1']]);
});
test('non-links cannot be opened', () => {
  const { dom } = open('result', { value: 'просто текст', browsers: [{ name: 'По умолчанию' }] });
  assert.equal(dom.window.document.querySelector('.actions button:last-child').disabled, true);
});
test('pin lock disables zoom controls while opacity remains available', () => {
  const { dom, calls, setLocked } = open('pin', { image: 'data:image/png;base64,AA==' });
  setLocked(true); const buttons = dom.window.document.querySelectorAll('.tool');
  assert.equal(buttons[0].disabled, true); assert.equal(buttons[1].disabled, true);
  dom.window.document.querySelector('.pin-shell').dispatchEvent(new dom.window.WheelEvent('wheel', { altKey: true, deltaY: -10, bubbles: true }));
  assert.deepEqual(calls, [['opacity', 10]]);
});
test('settings records and saves a keyboard shortcut', () => {
  const { dom, calls } = open('settings', { bindings: { capture: 'Command+Shift+2', create: 'Command+Alt+1', scan: 'Command+Alt+2' }, startup: false });
  const input = dom.window.document.querySelector('input[readonly]');
  input.dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'k', metaKey: true, shiftKey: true, bubbles: true }));
  dom.window.document.querySelector('.binding button').click();
  assert.deepEqual(calls, [['binding', 'capture', 'Command+Shift+K']]);
});
