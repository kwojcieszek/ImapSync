'use strict';
const express = require('express');
const session = require('express-session');
const path = require('path');
const fs = require('fs');
const appConfig = require('./config.json');

const authRoutes = require('./routes/auth');
const pairsRoutes = require('./routes/pairs');
const settingsRoutes = require('./routes/settings');
const systemRoutes = require('./routes/system');
const authMiddleware = require('./middleware/auth');

const app = express();

// Ensure log directory exists
const logDir = path.resolve(__dirname, appConfig.logDir || './logs');
if (!fs.existsSync(logDir)) {
    fs.mkdirSync(logDir, { recursive: true });
}

app.use(express.json());
app.use(express.urlencoded({ extended: true }));

app.use(session({
    secret: appConfig.sessionSecret || 'change-me',
    resave: false,
    saveUninitialized: false,
    cookie: { secure: false, maxAge: 8 * 60 * 60 * 1000 }
}));

app.use(express.static(path.join(__dirname, 'public')));

app.use('/api/auth', authRoutes);
app.use('/api/pairs', authMiddleware, pairsRoutes);
app.use('/api/settings', authMiddleware, settingsRoutes);
app.use('/api/system', authMiddleware, systemRoutes);

app.get('*', (req, res) => {
    res.sendFile(path.join(__dirname, 'public', 'index.html'));
});

const port = appConfig.port;
app.listen(port, () => {
    console.log(`ImapSync Web UI running at http://localhost:${port}`);
});
