'use strict';
const express = require('express');
const config = require('../utils/configManager');
const audit = require('../utils/auditLogger');
const appConfig = require('../config.json');

const router = express.Router();

function user(req) {
    return req.session.user?.username || 'unknown';
}

// GET /api/settings
router.get('/', (req, res) => {
    try {
        const s = config.getSettings();
        // Return without MailboxPairs (managed separately)
        const { MailboxPairs, ...rest } = s;
        res.json({ ...rest, restartCommand: appConfig.restartCommand || 'systemctl restart {service}', workerLogPath: appConfig.workerLogPath || '' });
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// PUT /api/settings
router.put('/', (req, res) => {
    try {
        const current = config.getSettings();
        const { StateFilePath, ErrorNotification } = req.body;

        current.StateFilePath = (StateFilePath || '').trim() || current.StateFilePath;

        if (ErrorNotification !== undefined) {
            if (ErrorNotification === null || ErrorNotification.Host === '') {
                current.ErrorNotification = null;
            } else {
                current.ErrorNotification = {
                    Host: (ErrorNotification.Host || '').trim(),
                    Port: Number(ErrorNotification.Port) || 587,
                    UseSsl: Boolean(ErrorNotification.UseSsl),
                    Username: (ErrorNotification.Username || '').trim(),
                    Password: ErrorNotification.Password || '',
                    From: (ErrorNotification.From || '').trim(),
                    To: (ErrorNotification.To || '').trim()
                };
            }
        }

        config.saveSettings(current);
        audit.info(user(req), 'EDIT_SETTINGS', `StateFilePath: ${current.StateFilePath}`);

        const { MailboxPairs, ...rest } = current;
        res.json(rest);
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

module.exports = router;
