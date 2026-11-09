'use strict';
const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('screenpin', {
  onInitial: handler => ipcRenderer.on('initial', (_, data) => handler(data)),
  onLocked: handler => ipcRenderer.on('locked', (_, locked) => handler(locked)),
  close: () => ipcRenderer.send('close-window'),
  lock: () => ipcRenderer.send('lock'),
  zoom: factor => ipcRenderer.send('zoom', factor),
  opacity: delta => ipcRenderer.send('opacity', delta),
  save: () => ipcRenderer.invoke('save'),
  copyImage: () => ipcRenderer.send('copy-image'),
  copyText: () => ipcRenderer.send('copy-text'),
  dragImage: () => ipcRenderer.send('drag-image'),
  openLink: index => ipcRenderer.invoke('open-link', index),
  binding: (key, binding) => ipcRenderer.invoke('binding', key, binding),
  startup: enabled => ipcRenderer.invoke('startup', enabled),
  settingsAction: action => ipcRenderer.invoke('settings-action', action)
});
