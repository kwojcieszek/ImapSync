'use strict';
const fs = require('fs');
const path = require('path');
const appConfig = require('../config.json');

const mailboxesPath = path.resolve(__dirname, '..', appConfig.mailboxesJsonPath);

function read() {
    const raw = fs.readFileSync(mailboxesPath, 'utf8');
    return JSON.parse(raw);
}

function write(data) {
    fs.writeFileSync(mailboxesPath, JSON.stringify(data, null, 2), 'utf8');
}

function getSettings() {
    return read().SyncSettings;
}

function saveSettings(settings) {
    const data = read();
    data.SyncSettings = settings;
    write(data);
}

function getPairs() {
    return getSettings().MailboxPairs || [];
}

function savePairs(pairs) {
    const data = read();
    data.SyncSettings.MailboxPairs = pairs;
    write(data);
}

function getMailboxesPath() {
    return mailboxesPath;
}

module.exports = { getSettings, saveSettings, getPairs, savePairs, getMailboxesPath };
