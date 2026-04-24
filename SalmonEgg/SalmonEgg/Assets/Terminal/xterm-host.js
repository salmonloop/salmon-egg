(function () {
  const terminal = new Terminal({
    convertEol: true,
    cursorBlink: false,
    disableStdin: true,
    fontFamily: 'Cascadia Mono, Consolas, monospace',
    fontSize: 13,
    lineHeight: 1.2,
    theme: {
      background: '#0b0d0f',
      foreground: '#e6edf3',
      cursor: '#e6edf3',
      selectionBackground: '#24435f'
    }
  });

  terminal.open(document.getElementById('terminal'));

  function postWebViewMessage(message) {
    try {
      if (window.hasOwnProperty('chrome') && typeof chrome.webview !== 'undefined') {
        chrome.webview.postMessage(message);
      } else if (window.hasOwnProperty('unoWebView')) {
        unoWebView.postMessage(JSON.stringify(message));
      } else if (window.hasOwnProperty('webkit') && typeof webkit.messageHandlers !== 'undefined') {
        webkit.messageHandlers.unoWebView.postMessage(JSON.stringify(message));
      }
    } catch (error) {
      console.error('Failed to post terminal message', error);
    }
  }

  window.salmonTerminal = {
    setContent(text) {
      terminal.clear();
      terminal.write(text || '');
    },
    append(text) {
      terminal.write(text || '');
    },
    clear() {
      terminal.clear();
    },
    fit() {
      terminal.scrollToBottom();
    }
  };

  postWebViewMessage({ kind: 'ready' });
})();
