'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { PNG } = require('pngjs');
const { createQr, decodeImage, httpUrl } = require('../core');

test('QR round trip keeps a Unicode link', async () => {
  const original = 'https://example.org/привет?q=123';
  const dataUrl = await createQr(original);
  const png = PNG.sync.read(Buffer.from(dataUrl.split(',')[1], 'base64'));
  const bgra = Buffer.from(png.data);
  for (let i = 0; i < bgra.length; i += 4) { const red = bgra[i]; bgra[i] = bgra[i + 2]; bgra[i + 2] = red; }
  const image = { getSize: () => ({ width: png.width, height: png.height }), toBitmap: () => bgra };
  assert.equal(decodeImage(image), original);
});
test('blank image is not recognized as a QR', () => {
  const image = { getSize: () => ({ width: 40, height: 40 }), toBitmap: () => Buffer.alloc(40 * 40 * 4, 255) };
  assert.equal(decodeImage(image), null);
});
test('opening a QR only accepts absolute HTTP and HTTPS links', () => {
  assert.equal(httpUrl(' https://example.org/a ').hostname, 'example.org');
  for (const value of ['javascript:alert(1)', 'file:///etc/passwd', 'example.org', '', 'mailto:a@example.org']) assert.equal(httpUrl(value), null);
});
