// Helpers for the widget. The views call them with IJSRuntime; the self-test calls them with ExecuteScriptAsync.
window.runner = {
  // System: no attribute, the page follows Windows. Light, Dark: data-theme. Another name: data-theme and its theme file.
  setTheme(name) {
    const root = document.documentElement;
    const theme = (name || 'System').toLowerCase();
    if (theme === 'system') root.removeAttribute('data-theme'); else root.setAttribute('data-theme', theme);
    let link = document.getElementById('theme-file');
    const custom = !['system', 'light', 'dark'].includes(theme);
    if (custom) {
      if (!link) { link = document.createElement('link'); link.id = 'theme-file'; link.rel = 'stylesheet'; document.head.appendChild(link); }
      link.href = `_content/App.Views/themes/${theme}.css`;
    } else if (link) {
      link.remove();
    }
  },
  scrollTop(id) {
    const el = document.getElementById(id);
    if (el) el.scrollTop = 0;
  },
};
