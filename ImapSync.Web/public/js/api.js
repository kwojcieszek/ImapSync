'use strict';

const api = {
  async login(username, password) {
    const r = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });
    return { ok: r.ok, data: await r.json() };
  },

  async logout() {
    await fetch('/api/auth/logout', { method: 'POST' });
  },

  async me() {
    const r = await fetch('/api/auth/me');
    if (!r.ok) { return null; }
    return r.json();
  },

  async getPairs() {
    const r = await fetch('/api/pairs');
    if (!r.ok) { return []; }
    return r.json();
  },

  async createPair(data) {
    const r = await fetch('/api/pairs', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    return { ok: r.ok, data: await r.json() };
  },

  async updatePair(name, data) {
    const r = await fetch(`/api/pairs/${encodeURIComponent(name)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    return { ok: r.ok, data: await r.json() };
  },

  async deletePair(name) {
    const r = await fetch(`/api/pairs/${encodeURIComponent(name)}`, { method: 'DELETE' });
    return { ok: r.ok, data: r.ok ? null : await r.json() };
  },

  async getSettings() {
    const r = await fetch('/api/settings');
    if (!r.ok) { return null; }
    return r.json();
  },

  async saveSettings(data) {
    const r = await fetch('/api/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    return { ok: r.ok, data: await r.json() };
  },

  async saveLogPath(logPath) {
    const r = await fetch('/api/system/logpath', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ logPath }),
    });
    return { ok: r.ok, data: await r.json() };
  },

  async getLog(lines = 200) {
    const r = await fetch(`/api/system/log?lines=${lines}`);
    return { ok: r.ok, data: await r.json() };
  },

  async restartService(serviceName) {
    const r = await fetch('/api/system/restart', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ serviceName }),
    });
    return { ok: r.ok, data: await r.json() };
  },
};
