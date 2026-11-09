'use strict';
const QRCode = require('qrcode');
const jsQR = require('jsqr');

function decodeImage(image) {
  const size = image.getSize(), raw = image.toBitmap(), pixels = new Uint8ClampedArray(raw.length);
  for (let i = 0; i < raw.length; i += 4) { pixels[i] = raw[i + 2]; pixels[i + 1] = raw[i + 1]; pixels[i + 2] = raw[i]; pixels[i + 3] = raw[i + 3]; }
  const result = jsQR(pixels, size.width, size.height, { inversionAttempts: 'attemptBoth' });
  return result ? result.data : null;
}
function httpUrl(text) {
  try { const url = new URL(text.trim()); return ['http:', 'https:'].includes(url.protocol) ? url : null; }
  catch (_) { return null; }
}
async function createQr(text) { return QRCode.toDataURL(text, { width: 600, margin: 4, errorCorrectionLevel: 'M' }); }
module.exports = { decodeImage, httpUrl, createQr };
