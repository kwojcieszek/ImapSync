'use strict';
const express = require('express');
const config = require('../utils/configManager');
const audit = require('../utils/auditLogger');

const router = express.Router();

function user(req) {
    return req.session.user?.username || 'unknown';
}

function validateCredentials(creds, label) {
    if (!creds || !creds.Host || !creds.Username || !creds.Password) {
        return `${label}: Host, Username and Password are required`;
    }
    const port = Number(creds.Port);
    if (!port || port < 1 || port > 65535) {
        return `${label}: Port must be between 1 and 65535`;
    }
    return null;
}

function validatePair(pair) {
    if (!pair.Name || !pair.Name.trim()) return 'Name is required';
    if (!pair.IntervalMinutes || pair.IntervalMinutes < 1) return 'IntervalMinutes must be at least 1';
    const srcErr = validateCredentials(pair.Source, 'Source');
    if (srcErr) return srcErr;
    if (!Array.isArray(pair.Destinations) || pair.Destinations.length === 0) return 'At least one destination is required';
    for (let i = 0; i < pair.Destinations.length; i++) {
        const err = validateCredentials(pair.Destinations[i], `Destination ${i + 1}`);
        if (err) return err;
    }
    return null;
}

function normalizeCreds(c) {
    return {
        Host: (c.Host || '').trim(),
        Port: Number(c.Port) || 993,
        UseSsl: Boolean(c.UseSsl),
        Username: (c.Username || '').trim(),
        Password: c.Password || ''
    };
}

function normalizePair(raw) {
    return {
        Name: (raw.Name || '').trim(),
        IntervalMinutes: Number(raw.IntervalMinutes) || 0,
        Source: normalizeCreds(raw.Source || {}),
        Destinations: (raw.Destinations || []).map(normalizeCreds)
    };
}

// GET /api/pairs
router.get('/', (req, res) => {
    try {
        res.json(config.getPairs());
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// POST /api/pairs
router.post('/', (req, res) => {
    try {
        const pair = normalizePair(req.body);
        const err = validatePair(pair);
        if (err) return res.status(400).json({ error: err });

        const pairs = config.getPairs();
        if (pairs.find(p => p.Name === pair.Name)) {
            return res.status(409).json({ error: `Pair "${pair.Name}" already exists` });
        }

        pairs.push(pair);
        config.savePairs(pairs);
        audit.info(user(req), 'ADD_PAIR', `Name: ${pair.Name}`);
        res.status(201).json(pair);
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// PUT /api/pairs/:name
router.put('/:name', (req, res) => {
    try {
        const oldName = decodeURIComponent(req.params.name);
        const pair = normalizePair(req.body);
        const err = validatePair(pair);
        if (err) return res.status(400).json({ error: err });

        const pairs = config.getPairs();
        const idx = pairs.findIndex(p => p.Name === oldName);
        if (idx === -1) return res.status(404).json({ error: `Pair "${oldName}" not found` });

        if (pair.Name !== oldName && pairs.find(p => p.Name === pair.Name)) {
            return res.status(409).json({ error: `Pair "${pair.Name}" already exists` });
        }

        pairs[idx] = pair;
        config.savePairs(pairs);
        audit.info(user(req), 'EDIT_PAIR', `Old: ${oldName} → New: ${pair.Name}`);
        res.json(pair);
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

// DELETE /api/pairs/:name
router.delete('/:name', (req, res) => {
    try {
        const name = decodeURIComponent(req.params.name);
        const pairs = config.getPairs();
        const idx = pairs.findIndex(p => p.Name === name);
        if (idx === -1) return res.status(404).json({ error: `Pair "${name}" not found` });

        pairs.splice(idx, 1);
        config.savePairs(pairs);
        audit.info(user(req), 'DELETE_PAIR', `Name: ${name}`);
        res.json({ ok: true });
    } catch (e) {
        res.status(500).json({ error: e.message });
    }
});

module.exports = router;
