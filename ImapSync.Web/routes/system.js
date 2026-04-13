'use strict';
const express = require('express');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');
const audit = require('../utils/auditLogger');

const CONFIG_PATH = path.join(__dirname, '../config.json');

function getAppConfig() {
    return JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8'));
}

const router = express.Router();

function user(req) {
    return req.session.user?.username || 'unknown';
}

// POST /api/system/restart  { serviceName }
router.post('/restart', (req, res) => {
    const appConfig = getAppConfig();
    const commandTemplate = (appConfig.restartCommand || 'systemctl restart {service}').trim();

    const { serviceName } = req.body;
    const name = (serviceName || '').trim().replace(/[^a-zA-Z0-9._@\-]/g, '');

    if (commandTemplate.includes('{service}') && !name) {
        return res.status(400).json({ error: 'serviceName is required for this command' });
    }

    const command = commandTemplate.replace('{service}', name);

    audit.info(user(req), 'RESTART_SERVICE', command);

    exec(command, (err, stdout, stderr) => {
        const output = [stdout, stderr].filter(Boolean).join('\n').trim();
        if (err) {
            audit.error(user(req), 'RESTART_SERVICE_ERROR', `${command} — ${err.message}`);
            return res.status(500).json({ error: err.message, output });
        }
        audit.info(user(req), 'RESTART_SERVICE_OK', command);
        res.json({ ok: true, output });
    });
});

// PUT /api/system/logpath  { logPath }
router.put('/logpath', (req, res) => {
    try {
        const { logPath } = req.body;
        if (logPath === undefined) return res.status(400).json({ error: 'logPath is required' });
        const appConfig = getAppConfig();
        appConfig.workerLogPath = (logPath || '').trim();
        fs.writeFileSync(CONFIG_PATH, JSON.stringify(appConfig, null, 2), 'utf8');
        audit.info(user(req), 'EDIT_LOG_PATH', appConfig.workerLogPath);
        res.json({ ok: true });
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// GET /api/system/log?lines=200
router.get('/log', (req, res) => {
    try {
        const appConfig = getAppConfig();
        const logPath = (appConfig.workerLogPath || '').trim();
        if (!logPath) return res.status(400).json({ error: 'Log path not configured' });
        if (!fs.existsSync(logPath)) return res.status(404).json({ error: `File not found: ${logPath}` });
        const lines = Math.min(parseInt(req.query.lines) || 200, 1000);
        const content = fs.readFileSync(logPath, 'utf8');
        const all = content.split('\n');
        const tail = all.slice(-lines).join('\n');
        res.json({ ok: true, content: tail });
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

module.exports = router;
