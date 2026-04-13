'use strict';
const fs = require('fs');
const path = require('path');
const appConfig = require('../config.json');

const logDir = path.resolve(__dirname, '..', appConfig.logDir || './logs');

function ensureLogDir() {
    if (!fs.existsSync(logDir)) {
        fs.mkdirSync(logDir, { recursive: true });
    }
}

function getLogFilePath() {
    const date = new Date().toISOString().slice(0, 10);
    return path.join(logDir, `audit-${date}.log`);
}

function log(level, username, action, details = '') {
    ensureLogDir();
    const timestamp = new Date().toISOString().replace('T', ' ').slice(0, 19);
    const line = `[${timestamp}] ${level.padEnd(5)} | ${String(username || 'anonymous').padEnd(20)} | ${String(action).padEnd(25)} | ${details}\n`;
    fs.appendFileSync(getLogFilePath(), line, 'utf8');
}

module.exports = {
    info: (username, action, details) => log('INFO', username, action, details),
    warn: (username, action, details) => log('WARN', username, action, details),
    error: (username, action, details) => log('ERROR', username, action, details),
};
