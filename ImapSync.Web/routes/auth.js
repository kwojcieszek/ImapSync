'use strict';
const express = require('express');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const audit = require('../utils/auditLogger');

const router = express.Router();

function loadUsers() {
    const raw = fs.readFileSync(path.resolve(__dirname, '../data/users.json'), 'utf8');
    return JSON.parse(raw).users;
}

function md5(str) {
    return crypto.createHash('md5').update(str).digest('hex');
}

function getIp(req) {
    return req.headers['x-forwarded-for'] || req.socket.remoteAddress || 'unknown';
}

// POST /api/auth/login
router.post('/login', (req, res) => {
    const { username, password, token } = req.body;
    const users = loadUsers();
    const ip = getIp(req);

    let user = null;

    if (token) {
        user = users.find(u => u.token === token);
    } else if (username && password) {
        const hash = md5(password);
        user = users.find(u => u.username === username && u.passwordMd5 === hash);
    }

    if (!user) {
        audit.warn(username || 'unknown', 'LOGIN_FAILED', `IP: ${ip}`);
        return res.status(401).json({ error: 'Invalid credentials' });
    }

    req.session.user = { username: user.username, role: user.role, displayName: user.displayName };
    audit.info(user.username, 'LOGIN', `IP: ${ip}`);
    res.json({ username: user.username, role: user.role, displayName: user.displayName });
});

// POST /api/auth/logout
router.post('/logout', (req, res) => {
    const username = req.session.user?.username;
    req.session.destroy();
    audit.info(username || 'unknown', 'LOGOUT', '');
    res.json({ ok: true });
});

// GET /api/auth/me
router.get('/me', (req, res) => {
    if (req.session && req.session.user) {
        return res.json(req.session.user);
    }
    res.status(401).json({ error: 'Not authenticated' });
});

module.exports = router;
