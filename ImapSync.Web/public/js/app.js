'use strict';

function app() {
  return {
    // --- state ---
    user: null,
    view: 'dashboard',
    lang: localStorage.getItem('lang') || 'en',

    loginForm: { username: '', password: '' },
    loginError: '',
    loginLoading: false,

    toast: { visible: false, message: '', type: 'success' },

    pairs: [],
    generalSettings: { StateFilePath: '' },
    smtpSettings: { Host: '', Port: 587, UseSsl: false, Username: '', Password: '', From: '', To: '' },

    serviceName: '',
    restartCommand: '',
    restartOutput: '',
    restartError: '',
    restartLoading: false,

    workerLogPath: '',
    logPathSaving: false,
    logContent: '',
    logError: '',
    logLoading: false,
    logLines: 200,
    logAutoRefresh: false,
    _logTimer: null,

    pairModal: {
      open: false, isEdit: false, editName: '', saving: false, error: '',
      form: { Name: '', Source: { Host: '', Port: 993, UseSsl: true, Username: '', Password: '' }, Destinations: [] },
    },

    // --- i18n ---
    t(key) {
      const dict = TRANSLATIONS[this.lang] || TRANSLATIONS['en'];
      return dict[key] || TRANSLATIONS['en'][key] || key;
    },

    saveLang() {
      localStorage.setItem('lang', this.lang);
    },

    // --- lifecycle ---
    async init() {
      await this._loadViews();
      try {
        this.user = await api.me();
        if (this.user) {
          await Promise.all([this.loadPairs(), this.loadSettings()]);
        }
      } catch {}

      this.$watch('view', (newView, oldView) => {
        if (oldView === 'log' && this.logAutoRefresh) {
          this.toggleLogAutoRefresh();
        }
        if (newView === 'log') {
          this.loadLog();
        }
      });
    },

    async _loadViews() {
      const views = [
        'login',
        'dashboard',
        'pairs',
        'settings',
        'system',
        'log',
        'modal-pair',
      ];
      await Promise.all(views.map(async (name) => {
        try {
          const resp = await fetch(`/views/${name}.html`);
          const html = await resp.text();
          const el = document.getElementById(`view-${name}`);
          if (el) {
            el.innerHTML = html;
            Alpine.initTree(el);
          }
        } catch (e) {
          console.error(`Failed to load view "${name}":`, e);
        }
      }));
    },

    // --- auth ---
    async login() {
      this.loginError = '';
      this.loginLoading = true;
      try {
        const { ok, data } = await api.login(this.loginForm.username, this.loginForm.password);
        if (!ok) { this.loginError = data.error || this.t('loginError'); return; }
        this.user = data;
        await Promise.all([this.loadPairs(), this.loadSettings()]);
      } catch { this.loginError = this.t('connectionError'); }
      finally { this.loginLoading = false; }
    },

    async logout() {
      await api.logout();
      this.user = null;
      this.loginForm = { username: '', password: '' };
    },

    // --- navigation ---
    viewTitle() {
      return {
        dashboard: this.t('dashboardTitle'),
        pairs:     this.t('pairsTitle'),
        settings:  this.t('settingsTitle'),
        system:    this.t('systemTitle'),
        log:       this.t('logTitle'),
      }[this.view] || '';
    },

    viewSubtitle() {
      return {
        dashboard: this.t('dashboardSubtitle'),
        pairs:     this.t('pairsSubtitle'),
        settings:  this.t('settingsSubtitle'),
        system:    this.t('systemSubtitle'),
        log:       this.t('logSubtitle'),
      }[this.view] || '';
    },

    // --- pairs ---
    totalDestinations() {
      return this.pairs.reduce((s, p) => s + (p.Destinations?.length || 0), 0);
    },

    async loadPairs() {
      try { this.pairs = await api.getPairs(); } catch {}
    },

    emptyPairForm() {
      return {
        Name: '',
        IntervalMinutes: 5,
        Source: { Host: '', Port: 993, UseSsl: true, Username: '', Password: '' },
        Destinations: [{ Host: '', Port: 993, UseSsl: true, Username: '', Password: '' }],
      };
    },

    openAddPair() {
      this.pairModal = { open: true, isEdit: false, editName: '', saving: false, error: '', form: this.emptyPairForm() };
    },

    openEditPair(pair) {
      this.pairModal = {
        open: true, isEdit: true, editName: pair.Name, saving: false, error: '',
        form: JSON.parse(JSON.stringify(pair)),
      };
    },

    addDestination() {
      this.pairModal.form.Destinations.push({ Host: '', Port: 993, UseSsl: true, Username: '', Password: '' });
    },

    removeDestination(i) {
      this.pairModal.form.Destinations.splice(i, 1);
    },

    async savePair() {
      this.pairModal.saving = true;
      this.pairModal.error = '';
      try {
        const { ok, data } = this.pairModal.isEdit
          ? await api.updatePair(this.pairModal.editName, this.pairModal.form)
          : await api.createPair(this.pairModal.form);
        if (!ok) { this.pairModal.error = data.error || this.t('saveError'); return; }
        await this.loadPairs();
        this.pairModal.open = false;
        this.showToast(this.pairModal.isEdit ? this.t('pairUpdated') : this.t('pairAdded'));
      } catch { this.pairModal.error = this.t('connectionError'); }
      finally { this.pairModal.saving = false; }
    },

    async deletePair(name) {
      if (!confirm(`${this.t('confirmDelete')} "${name}"?`)) { return; }
      try {
        const { ok, data } = await api.deletePair(name);
        if (!ok) { return this.showToast(data?.error || this.t('deleteError'), 'error'); }
        await this.loadPairs();
        this.showToast(this.t('pairDeleted'));
      } catch { this.showToast(this.t('connectionErr'), 'error'); }
    },

    // --- settings ---
    async loadSettings() {
      try {
        const s = await api.getSettings();
        if (!s) { return; }
        this.generalSettings = { StateFilePath: s.StateFilePath };
        this.smtpSettings = s.ErrorNotification
          ? { ...s.ErrorNotification }
          : { Host: '', Port: 587, UseSsl: false, Username: '', Password: '', From: '', To: '' };
        if (s.restartCommand !== undefined) { this.restartCommand = s.restartCommand; }
        if (s.workerLogPath !== undefined) { this.workerLogPath = s.workerLogPath; }
      } catch {}
    },

    restartCommandPreview() {
      const tpl = this.restartCommand || 'systemctl restart {service}';
      return tpl.replace('{service}', this.serviceName || '{service}');
    },

    async saveSettings() {
      try {
        const { ok, data } = await api.saveSettings({
          StateFilePath: this.generalSettings.StateFilePath,
        });
        if (!ok) { return this.showToast(data.error || this.t('saveError'), 'error'); }
        this.showToast(this.t('settingsSaved'));
      } catch { this.showToast(this.t('connectionErr'), 'error'); }
    },

    async saveSMTP() {
      try {
        const smtp = this.smtpSettings.Host ? this.smtpSettings : null;
        const { ok, data } = await api.saveSettings({
          StateFilePath:     this.generalSettings.StateFilePath,
          ErrorNotification: smtp,
        });
        if (!ok) { return this.showToast(data.error || this.t('smtpSaveError'), 'error'); }
        this.showToast(this.t('smtpSaved'));
      } catch { this.showToast(this.t('connectionErr'), 'error'); }
    },

    // --- system ---
    async saveLogPath() {
      this.logPathSaving = true;
      try {
        const { ok, data } = await api.saveLogPath(this.workerLogPath);
        if (!ok) { return this.showToast(data.error || this.t('saveError'), 'error'); }
        this.showToast(this.t('logPathSaved'));
      } catch { this.showToast(this.t('connectionError'), 'error'); }
      finally { this.logPathSaving = false; }
    },

    async loadLog() {
      this.logLoading = true; this.logError = '';
      try {
        const { ok, data } = await api.getLog(this.logLines);
        if (!ok) { this.logError = data.error; this.logContent = ''; }
        else { this.logContent = data.content; }
      } catch { this.logError = this.t('connectionError'); }
      finally { this.logLoading = false; }
    },

    toggleLogAutoRefresh() {
      this.logAutoRefresh = !this.logAutoRefresh;
      if (this.logAutoRefresh) {
        this.loadLog();
        this._logTimer = setInterval(() => this.loadLog(), 5000);
      } else {
        clearInterval(this._logTimer);
        this._logTimer = null;
      }
    },

    async restartService() {
      this.restartOutput = ''; this.restartError = ''; this.restartLoading = true;
      try {
        const { ok, data } = await api.restartService(this.serviceName);
        if (!ok) { this.restartError = data.error; if (data.output) { this.restartOutput = data.output; } }
        else {
          this.restartOutput = data.output || '';
          this.showToast(this.t('serviceRestarted'));
        }
      } catch { this.restartError = this.t('connectionError'); }
      finally { this.restartLoading = false; }
    },

    // --- ui helpers ---
    showToast(message, type = 'success') {
      this.toast = { visible: true, message, type };
      setTimeout(() => { this.toast.visible = false; }, 3000);
    },
  };
}
