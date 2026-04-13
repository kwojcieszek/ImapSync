'use strict';
const fs = require('fs');
const path = require('path');

function loadUsers() {
    const raw = fs.readFileSync(path.resolve(__dirname, '../data/users.json'), 'utf8');
    return JSON.parse(raw).users;
}

module.exports = function authMiddleware(req, res, next) {
    if (req.session && req.session.user) {
        return next();
    }

    const auth = req.headers['authorization'] || '';
    const token = auth.startsWith('Bearer ') ? auth.slice(7) : req.query.token;

    if (token) {
        const users = loadUsers();
        const user = users.find(u => u.token === token);
        if (user) {
            req.session.user = { username: user.username, role: user.role, displayName: user.displayName };
            return next();
        }
    }

    res.status(401).json({ error: 'Unauthorized' });
};
