(function () {
  const terminalElement = document.getElementById('terminal');
  const hostUrl = new URL(window.location.href);
  const hostId = hostUrl.searchParams.get('hostId') || 'default';
  const measurementCanvas = document.createElement('canvas');
  const measurementContext = measurementCanvas.getContext('2d');
  const lastResize = {
    cols: 0,
    rows: 0,
    width: 0,
    height: 0
  };
  let hostHeight = 0;
  let hostWidth = 0;
  let inputEnabled = true;
  let isDisposed = false;
  let resizeFrame = 0;
  let resizeObserver = null;
  let transportMode = 'pipe';
  let win32InputMode = false;

  const terminal = new Terminal({
    convertEol: true,
    cursorBlink: true,
    fontFamily: 'Cascadia Mono, Consolas, monospace',
    fontSize: 13,
    lineHeight: 1.2,
    scrollback: 5000,
    theme: {
      background: '#0b0d0f',
      foreground: '#e6edf3',
      cursor: '#e6edf3',
      selectionBackground: '#24435f'
    }
  });

  terminal.open(terminalElement);
  installTerminalModeHandlers();

  function postWebViewMessage(message) {
    try {
      const payload = Object.assign({ hostId }, message);
      if (typeof chrome !== 'undefined' && typeof chrome.webview !== 'undefined') {
        chrome.webview.postMessage(payload);
      } else if (typeof unoWebView !== 'undefined') {
        unoWebView.postMessage(JSON.stringify(payload));
      } else if (typeof webkit !== 'undefined' && typeof webkit.messageHandlers !== 'undefined') {
        webkit.messageHandlers.unoWebView.postMessage(JSON.stringify(payload));
      }
    } catch (error) {
      console.error('Failed to post terminal message', error);
    }
  }

  function reportError(code, message) {
    postWebViewMessage({
      kind: 'error',
      code,
      message
    });
  }

  function clampDimension(value) {
    const parsed = Number(value);
    return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : 0;
  }

  function measureCellSize() {
    const fontSize = Number(terminal.options.fontSize) || 13;
    const lineHeight = Number(terminal.options.lineHeight) || 1;

    if (measurementContext) {
      measurementContext.font = `${fontSize}px ${terminal.options.fontFamily || 'monospace'}`;
      return {
        width: Math.max(Math.ceil(measurementContext.measureText('W').width), 1),
        height: Math.max(Math.ceil(fontSize * lineHeight), 1)
      };
    }

    return {
      width: Math.max(Math.ceil(fontSize * 0.6), 1),
      height: Math.max(Math.ceil(fontSize * lineHeight), 1)
    };
  }

  function getViewportSize() {
    const width = hostWidth > 0 ? hostWidth : clampDimension(terminalElement.clientWidth);
    const height = hostHeight > 0 ? hostHeight : clampDimension(terminalElement.clientHeight);

    return {
      width,
      height
    };
  }

  function publishResize(reason) {
    if (isDisposed) {
      return;
    }

    const viewport = getViewportSize();
    if (viewport.width <= 0 || viewport.height <= 0) {
      return;
    }

    const cell = measureCellSize();
    const nextCols = Math.max(Math.floor(viewport.width / cell.width), 2);
    const nextRows = Math.max(Math.floor(viewport.height / cell.height), 1);

    if (nextCols !== terminal.cols || nextRows !== terminal.rows) {
      terminal.resize(nextCols, nextRows);
    }

    if (lastResize.cols === terminal.cols
      && lastResize.rows === terminal.rows
      && lastResize.width === viewport.width
      && lastResize.height === viewport.height) {
      return;
    }

    lastResize.cols = terminal.cols;
    lastResize.rows = terminal.rows;
    lastResize.width = viewport.width;
    lastResize.height = viewport.height;

    postWebViewMessage({
      kind: 'resize',
      cols: terminal.cols,
      rows: terminal.rows,
      width: viewport.width,
      height: viewport.height,
      reason
    });
  }

  function scheduleResize(reason) {
    if (isDisposed) {
      return;
    }

    if (resizeFrame !== 0) {
      window.cancelAnimationFrame(resizeFrame);
    }

    resizeFrame = window.requestAnimationFrame(() => {
      resizeFrame = 0;
      publishResize(reason);
    });
  }

  terminal.onData((data) => {
    if (isDisposed || !inputEnabled || !data) {
      return;
    }

    if (transportMode === 'pipe') {
      echoInput(data);
    }

    postWebViewMessage({
      kind: 'input',
      data
    });
  });

  function echoInput(data) {
    if (data === '\r') {
      terminal.write('\r\n');
      return;
    }

    if (data === '\u007f') {
      terminal.write('\b \b');
      return;
    }

    terminal.write(data);
  }

  function setInputEnabled(enabled) {
    inputEnabled = enabled !== false;
    terminal.options.cursorBlink = inputEnabled;
  }

  function setTransportMode(nextMode) {
    transportMode = nextMode === 'pseudoConsole' ? 'pseudoConsole' : 'pipe';
    if (transportMode !== 'pseudoConsole') {
      win32InputMode = false;
    }

    applyTransportOptions();
  }

  function applyTransportOptions() {
    terminal.options.convertEol = transportMode === 'pipe';
    terminal.options.windowsPty = transportMode === 'pseudoConsole'
      ? { backend: 'conpty' }
      : undefined;
  }

  function installTerminalModeHandlers() {
    if (!terminal.parser || typeof terminal.parser.registerCsiHandler !== 'function') {
      return;
    }

    terminal.parser.registerCsiHandler({ prefix: '?', final: 'h' }, (params) => {
      if (transportMode !== 'pseudoConsole' || params.length !== 1 || params[0] !== 9001) {
        return false;
      }

      win32InputMode = true;
      return true;
    });

    terminal.parser.registerCsiHandler({ prefix: '?', final: 'l' }, (params) => {
      if (transportMode !== 'pseudoConsole' || params.length !== 1 || params[0] !== 9001) {
        return false;
      }

      win32InputMode = false;
      return true;
    });
  }

  function dispatchHostCommand(command) {
    if (!command || typeof command.kind !== 'string') {
      reportError('invalid-command', 'Terminal host command is missing a kind.');
      return;
    }

    if (command.hostId && command.hostId !== hostId) {
      return;
    }

    setTransportMode(command.transportMode);

    switch (command.kind) {
      case 'replace':
        terminal.clear();
        terminal.write(command.text || '');
        scheduleResize('replace');
        break;
      case 'stdout':
      case 'stderr':
        terminal.write(command.text || '');
        break;
      case 'clear':
        terminal.clear();
        break;
      case 'setInputEnabled':
        setInputEnabled(command.enabled);
        break;
      case 'hostSize':
        hostWidth = clampDimension(command.width);
        hostHeight = clampDimension(command.height);
        scheduleResize('host');
        break;
      case 'focus':
        terminal.focus();
        break;
      case 'detach':
        setInputEnabled(false);
        break;
      default:
        reportError('unsupported-command', `Unsupported terminal host command: ${command.kind}`);
        break;
    }
  }

  window.salmonTerminal = {
    dispatch(command) {
      dispatchHostCommand(command);
    }
  };

  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => {
      scheduleResize('observer');
    });
    resizeObserver.observe(terminalElement);
  }

  window.addEventListener('resize', () => {
    scheduleResize('window');
  });

  if (typeof chrome !== 'undefined'
    && typeof chrome.webview !== 'undefined'
    && typeof chrome.webview.addEventListener === 'function') {
    chrome.webview.addEventListener('message', (event) => {
      dispatchHostCommand(event.data);
    });
  }

  if (document.fonts && document.fonts.ready && typeof document.fonts.ready.then === 'function') {
    document.fonts.ready.then(() => {
      scheduleResize('fonts');
    });
  }

  window.addEventListener('beforeunload', () => {
    isDisposed = true;
    if (resizeFrame !== 0) {
      window.cancelAnimationFrame(resizeFrame);
      resizeFrame = 0;
    }

    if (resizeObserver) {
      resizeObserver.disconnect();
      resizeObserver = null;
    }
  });

  terminal.focus();
  applyTransportOptions();
  window.requestAnimationFrame(() => {
    publishResize('ready');
    postWebViewMessage({
      kind: 'ready',
      cols: terminal.cols,
      rows: terminal.rows
    });
  });
})();
