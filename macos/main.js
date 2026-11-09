'use strict';

const { app, BrowserWindow, Tray, Menu, globalShortcut, ipcMain, clipboard, nativeImage, dialog, screen, systemPreferences } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const crypto = require('node:crypto');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const { decodeImage, httpUrl, createQr } = require('./core');
const execFileAsync = promisify(execFile);

const defaults = { capture: 'CommandOrControl+Shift+2', create: 'CommandOrControl+Alt+1', scan: 'CommandOrControl+Alt+2' };
let bindings = { ...defaults }, tray, settingsWindow, stopping = false, capturing = false;
const windows = new Map();
const settingPath = () => path.join(app.getPath('userData'), 'hotkeys.json');
const temporary = name => path.join(os.tmpdir(), `ScreenPin-${crypto.randomBytes(12).toString('hex')}-${name}`);
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

function loadBindings() {
  try {
    const data = JSON.parse(fs.readFileSync(settingPath(), 'utf8'));
    for (const key of Object.keys(defaults)) if (typeof data[key] === 'string') bindings[key] = data[key];
  } catch (_) { /* first launch or unreadable preferences */ }
}
function saveBindings() { fs.mkdirSync(path.dirname(settingPath()), { recursive: true }); fs.writeFileSync(settingPath(), JSON.stringify(bindings, null, 2)); }
function showError(message) { dialog.showMessageBox({ type: 'info', title: 'ScreenPin', message }); }
function createWindow(kind, width, height, data, options = {}) {
  const win = new BrowserWindow({
    width, height, show: false, frame: false, transparent: kind === 'pin', resizable: kind === 'pin',
    alwaysOnTop: true, skipTaskbar: kind !== 'settings', backgroundColor: '#11141d',
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true, nodeIntegration: false, sandbox: true },
    ...options
  });
  windows.set(win.id, { kind, data, locked: false });
  win.setMenuBarVisibility(false);
  win.loadFile(path.join(__dirname, 'window.html'));
  win.webContents.on('did-finish-load', () => { win.webContents.send('initial', { kind, data }); win.show(); });
  win.on('closed', () => windows.delete(win.id));
  return win;
}
function senderWindow(event, kinds) {
  const win = BrowserWindow.fromWebContents(event.sender);
  const state = win && windows.get(win.id);
  if (!state || !kinds.includes(state.kind)) throw new Error('Недоступное действие');
  return { win, state };
}
function makePin(image) {
  const size = image.getSize(), work = screen.getDisplayNearestPoint(screen.getCursorScreenPoint()).workAreaSize;
  const factor = Math.min(1, (work.width - 80) / size.width, (work.height - 80) / size.height);
  const width = Math.max(160, Math.round(size.width * factor)), height = Math.max(90, Math.round(size.height * factor));
  const win = createWindow('pin', width, height, { image: image.toDataURL() }, { minWidth: 160, minHeight: 90 });
  const state = windows.get(win.id); state.image = image; state.ratio = size.width / size.height;
  win.setAspectRatio(state.ratio);
}
async function capture(scan) {
  if (capturing) return; capturing = true;
  const temp = temporary('capture.png');
  const restoreSettings = settingsWindow && !settingsWindow.isDestroyed() && settingsWindow.isVisible();
  try {
    if (process.platform !== 'darwin') { showError('Выделение области в этой версии доступно на macOS.'); return; }
    if (restoreSettings) { settingsWindow.hide(); await delay(180); }
    await execFileAsync('/usr/sbin/screencapture', ['-i', '-x', temp]);
    if (!fs.existsSync(temp)) return;
    const image = nativeImage.createFromPath(temp);
    if (image.isEmpty()) return;
    if (scan) {
      const result = decodeImage(image);
      if (!result) { showError('QR-код не найден. Выдели его целиком вместе с белыми полями.'); return; }
      const choices = browserChoices();
      createWindow('result', 420, 380, { value: result, browsers: choices });
    } else makePin(image);
  } catch (error) { showError('Не удалось сделать снимок. Проверь разрешение на запись экрана в настройках macOS.\n\n' + error.message); }
  finally { try { fs.unlinkSync(temp); } catch (_) {} if (restoreSettings && settingsWindow && !settingsWindow.isDestroyed()) settingsWindow.show(); capturing = false; }
}
function browserChoices() {
  const found = [{ name: 'Браузер по умолчанию', app: null }];
  for (const name of ['Safari', 'Google Chrome', 'Microsoft Edge', 'Firefox', 'Brave Browser']) {
    const candidate = `/Applications/${name}.app`;
    if (fs.existsSync(candidate)) found.push({ name, app: candidate });
  }
  return found;
}
async function showQr(text) {
  if (!text || !text.trim()) { showError('Нет текста для создания QR-кода.'); return; }
  try {
    const image = await createQr(text);
    createWindow('create', 420, 525, { text, image });
  } catch (error) { showError('Не удалось создать QR-код: ' + error.message); }
}
async function createFromSelection() {
  if (process.platform !== 'darwin') { showError('Копирование выделения доступно на macOS.'); return; }
  if (!systemPreferences.isTrustedAccessibilityClient(true)) { showError('Разреши ScreenPin управление компьютером в разделе «Конфиденциальность и безопасность → Универсальный доступ», затем попробуй снова.'); return; }
  await delay(350);
  const script = ["ObjC.import('AppKit');", "var before = $.NSPasteboard.generalPasteboard.changeCount;", "Application('System Events').keystroke('c', {using:'command down'});", 'delay(0.5);', "String($.NSPasteboard.generalPasteboard.changeCount !== before);"].join('\n');
  try {
    const { stdout } = await execFileAsync('/usr/bin/osascript', ['-l', 'JavaScript', '-e', script]);
    if (stdout.trim() !== 'true') { showError('Не удалось скопировать выделение. Скопируй текст вручную и выбери «QR из буфера обмена» в меню ScreenPin.'); return; }
    await showQr(clipboard.readText());
  } catch (_) { showError('Не удалось скопировать выделение. Проверь разрешения «Универсальный доступ» и «Автоматизация» для ScreenPin или создай QR из буфера обмена.'); }
}
const actions = { capture: () => capture(false), scan: () => capture(true), create: createFromSelection };
function register(key, shortcut) { try { return globalShortcut.register(shortcut, actions[key]); } catch (_) { return false; } }
function changeShortcut(key, shortcut) {
  if (!Object.hasOwn(defaults, key) || typeof shortcut !== 'string' || shortcut.length > 80) return 'Неверное сочетание';
  const normalized = value => value.replace('CommandOrControl', process.platform === 'darwin' ? 'Command' : 'Control');
  if (normalized(shortcut) === normalized(bindings[key])) return '';
  if (!register(key, shortcut)) return 'Сочетание занято или не поддерживается';
  globalShortcut.unregister(bindings[key]); bindings[key] = shortcut;
  try { saveBindings(); } catch (_) { return 'Активно до выхода, сохранить не удалось'; }
  return '';
}
function showSettings() {
  if (settingsWindow && !settingsWindow.isDestroyed()) { settingsWindow.show(); settingsWindow.focus(); return; }
  settingsWindow = createWindow('settings', 480, 600, { bindings, startup: app.getLoginItemSettings().openAtLogin });
  settingsWindow.on('closed', () => { settingsWindow = null; });
}
function trayMenu() {
  return Menu.buildFromTemplate([
    { label: 'Новый снимок', click: () => capture(false) },
    { label: 'QR из буфера обмена', click: () => showQr(clipboard.readText()) },
    { label: 'Считать QR с экрана', click: () => capture(true) },
    { type: 'separator' }, { label: 'Настройки', click: showSettings },
    { label: 'Выход', click: () => { stopping = true; app.quit(); } }
  ]);
}
function handlers() {
  ipcMain.handle('binding', (e, key, value) => { senderWindow(e, ['settings']); return changeShortcut(key, value); });
  ipcMain.handle('startup', (e, enabled) => { senderWindow(e, ['settings']); app.setLoginItemSettings({ openAtLogin: enabled === true }); return app.getLoginItemSettings().openAtLogin; });
  ipcMain.handle('settings-action', (e, action) => { senderWindow(e, ['settings']); if (Object.hasOwn(actions, action)) actions[action](); });
  ipcMain.on('close-window', e => senderWindow(e, ['settings', 'pin', 'create', 'result']).win.close());
  ipcMain.on('lock', e => { const { win, state } = senderWindow(e, ['pin']); state.locked = !state.locked; win.setResizable(!state.locked); win.webContents.send('locked', state.locked); });
  ipcMain.on('zoom', (e, factor) => {
    const { win, state } = senderWindow(e, ['pin']); if (state.locked || typeof factor !== 'number') return;
    const [w] = win.getSize(), width = Math.max(160, Math.min(4000, Math.round(w * Math.max(0.8, Math.min(1.25, factor)))));
    win.setSize(width, Math.max(90, Math.round(width / state.ratio)), true);
  });
  ipcMain.on('opacity', (e, delta) => { const { win } = senderWindow(e, ['pin']); if (typeof delta === 'number') win.setOpacity(Math.max(0.25, Math.min(1, win.getOpacity() + (delta > 0 ? 0.08 : -0.08)))); });
  ipcMain.handle('save', async e => {
    const { win, state } = senderWindow(e, ['pin', 'create']);
    const image = state.kind === 'pin' ? state.image : nativeImage.createFromDataURL(state.data.image);
    const result = await dialog.showSaveDialog(win, { defaultPath: state.kind === 'pin' ? 'screenpin.png' : 'screenpin-qr.png', filters: [{ name: 'PNG', extensions: ['png'] }] });
    if (!result.canceled && result.filePath) fs.writeFileSync(result.filePath, image.toPNG());
  });
  ipcMain.on('copy-image', e => { const { state } = senderWindow(e, ['create']); clipboard.writeImage(nativeImage.createFromDataURL(state.data.image)); });
  ipcMain.on('copy-text', e => { const { state } = senderWindow(e, ['result']); clipboard.writeText(state.data.value); });
  ipcMain.on('drag-image', e => {
    const { state } = senderWindow(e, ['create']); const image = nativeImage.createFromDataURL(state.data.image);
    const file = temporary('qr.png'); fs.writeFileSync(file, image.toPNG()); e.sender.startDrag({ file, icon: image.resize({ width: 64, height: 64 }) });
  });
  ipcMain.handle('open-link', async (e, index) => {
    const { state } = senderWindow(e, ['result']);
    const url = httpUrl(state.data.value); if (!url) return 'Открывать можно только ссылки http:// и https://.';
    const choice = state.data.browsers[Number(index)]; if (!choice) return 'Выбери браузер.';
    try { await execFileAsync('/usr/bin/open', choice.app ? ['-a', choice.app, url.href] : [url.href]); return ''; }
    catch (_) { return 'Не удалось открыть ссылку в браузере.'; }
  });
}
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', showSettings);
  app.whenReady().then(() => {
    if (process.platform === 'darwin') app.dock.hide();
    loadBindings(); handlers();
    const trayImage = nativeImage.createFromPath(path.join(__dirname, 'assets', 'tray.png'));
    if (process.platform === 'darwin') trayImage.setTemplateImage(true);
    tray = new Tray(trayImage);
    tray.setToolTip('ScreenPin'); tray.setContextMenu(trayMenu());
    for (const key of Object.keys(bindings)) if (!register(key, bindings[key])) showError(`Сочетание ${bindings[key]} занято. Измени его в настройках ScreenPin.`);
  });
  app.on('window-all-closed', () => { if (stopping) app.quit(); });
  app.on('will-quit', () => globalShortcut.unregisterAll());
}
