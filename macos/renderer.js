'use strict';
const api = window.screenpin;
const root = document.getElementById('app');
function element(tag, cls, text) { const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node; }
function button(text, cls, action) { const el = element('button', cls, text); el.type = 'button'; el.addEventListener('click', action); return el; }
function shell(title, subtitle, extra) {
  const box = element('div', `window ${extra || ''}`), header = element('div', 'heading');
  header.append(element('h1', 'title', title)); if (subtitle) header.append(element('p', 'sub', subtitle));
  box.append(header, button('×', 'close', () => api.close())); root.append(box); return box;
}
function actions(box, left, leftAction, right, rightAction) {
  const row = element('div', 'actions'); row.append(button(left, 'button', leftAction), button(right, 'button primary', rightAction)); box.append(row); return row;
}
function result(data) {
  const box = shell('QR распознан'), text = data.value; let url;
  try { url = new URL(text.trim()); } catch (_) {}
  const link = url && ['http:', 'https:'].includes(url.protocol);
  box.append(element('div', 'label', link ? 'ССЫЛКА' : 'СОДЕРЖИМОЕ'));
  const card = element('div', 'card'); card.append(element('div', 'domain', link ? url.host : 'Текст из QR-кода'));
  const content = element('textarea', 'content'); content.value = text; content.readOnly = true; card.append(content); box.append(card);
  box.append(element('div', 'label muted', 'Открыть в браузере'));
  const select = element('select', 'select'); data.browsers.forEach((browser, index) => { const option = element('option', '', browser.name); option.value = String(index); select.append(option); }); box.append(select);
  const row = actions(box, 'Копировать', () => api.copyText(), 'Открыть ссылку ↗', async () => { const error = await api.openLink(select.value); note.textContent = error; });
  if (!link) row.lastChild.disabled = true;
  const note = element('div', 'error-text'); box.append(note);
}
function create(data) {
  const box = shell('QR-код', 'Перетащи код в чат или сохрани его', 'create');
  const image = element('img', 'qr-preview'); image.src = data.image; image.draggable = false;
  image.addEventListener('mousedown', event => { if (event.button !== 0) return; let moved = false; const move = e => { if (!moved && (Math.abs(e.screenX - event.screenX) > 5 || Math.abs(e.screenY - event.screenY) > 5)) { moved = true; window.removeEventListener('mousemove', move); api.dragImage(); } }; window.addEventListener('mousemove', move); window.addEventListener('mouseup', () => window.removeEventListener('mousemove', move), { once: true }); });
  const text = element('input', 'preview-text'); text.value = data.text; text.readOnly = true;
  box.append(image, text); actions(box, 'Копировать', () => api.copyImage(), 'Сохранить PNG', () => api.save());
}
function pin(data) {
  document.body.className = 'pin'; const wrapper = element('div', 'pin-shell'); const img = element('img', 'pin-image'); img.src = data.image; img.draggable = false;
  const bar = element('div', 'toolbar');
  const minus = button('−', 'tool', () => api.zoom(1 / 1.2)), plus = button('+', 'tool', () => api.zoom(1.2));
  const lock = button('⌖', 'tool', () => api.lock()); lock.title = 'Закрепить';
  const save = button('↓', 'tool', () => api.save()), close = button('×', 'tool', () => api.close());
  minus.title = 'Уменьшить'; plus.title = 'Увеличить'; save.title = 'Сохранить PNG'; close.title = 'Закрыть'; bar.append(minus, plus, lock, save, close); wrapper.append(img, bar); root.append(wrapper);
  wrapper.addEventListener('wheel', event => { event.preventDefault(); if (event.altKey) api.opacity(-event.deltaY); else api.zoom(event.deltaY < 0 ? 1.12 : 1 / 1.12); }, { passive: false });
  api.onLocked(locked => { wrapper.classList.toggle('locked', locked); lock.classList.toggle('active', locked); minus.disabled = locked; plus.disabled = locked; lock.title = locked ? 'Открепить' : 'Закрепить'; });
}
function shortcut(event) {
  const modifiers = []; if (event.metaKey) modifiers.push('Command'); if (event.ctrlKey) modifiers.push('Control'); if (event.altKey) modifiers.push('Alt'); if (event.shiftKey) modifiers.push('Shift');
  let key = event.key.toUpperCase(); if (!modifiers.length || !(/^[A-Z0-9]$/.test(key) || /^F([1-9]|10|11)$/.test(key))) return null;
  return [...modifiers, key].join('+');
}
function pretty(value) { return value.replace('CommandOrControl', '⌘').replace('Command', '⌘').replace('Control', '⌃').replace('Shift', '⇧').replace('Alt', '⌥').replaceAll('+', ' '); }
function settings(data) {
  const box = shell('ScreenPin', 'Всё важное — перед глазами', 'settings');
  const items = [['capture', 'Снимок экрана'], ['create', 'Создать QR из выделения'], ['scan', 'Считать QR с экрана']];
  for (const [key, label] of items) {
    const card = element('div', 'card'); card.append(element('h3', '', label)); const group = element('div', 'binding'); const input = element('input'); input.readOnly = true;
    let candidate = data.bindings[key]; input.value = pretty(candidate); const note = element('div', 'hint', 'Нажми на поле и введи сочетание');
    input.addEventListener('keydown', event => { event.preventDefault(); const value = shortcut(event); if (!value) { note.textContent = 'Используй модификатор и букву, цифру или F1–F11'; return; } candidate = value; input.value = pretty(value); note.textContent = 'Нажми «Сохранить»'; });
    group.append(input, button('Сохранить', 'button', async () => { const error = await api.binding(key, candidate); note.textContent = error || 'Сохранено'; note.classList.toggle('error', Boolean(error)); input.blur(); })); card.append(group, note); box.append(card);
  }
  const startup = element('label', 'startup'); const check = element('input'); check.type = 'checkbox'; check.checked = data.startup; check.addEventListener('change', async () => { check.checked = await api.startup(check.checked); }); startup.append(check, document.createTextNode('Запускать вместе с macOS')); box.append(startup);
  actions(box, 'Снимок', () => api.settingsAction('capture'), 'Считать QR', () => api.settingsAction('scan'));
}
api.onInitial(({ kind, data }) => { if (kind === 'pin') pin(data); else if (kind === 'create') create(data); else if (kind === 'result') result(data); else if (kind === 'settings') settings(data); });
