(function () {
  const OVERLAY_ID = 'hts-split-overlay';
  const STYLE_ID = 'hts-preview-viewport-style';
  const MIN_GAP = 1;
  const LINE_HIT_HEIGHT = 20;

  let state = {
    viewportWidth: 1280,
    slideHeight: 720,
    splitYs: [],
    totalHeight: 0,
    scale: 1,
    dragging: false
  };

  function getTotalHeight() {
    return Math.max(
      document.body?.scrollHeight || 0,
      document.documentElement?.scrollHeight || 0,
      1
    );
  }

  function computeDefaultSplits(totalHeight, slideHeight) {
    const ys = [];
    for (let y = slideHeight; y < totalHeight; y += slideHeight) ys.push(y);
    return ys;
  }

  function updateScale() {
    const available = Math.max(1, window.innerWidth || state.viewportWidth);
    state.scale = Math.min(1, available / state.viewportWidth);
    document.documentElement.style.setProperty('--hts-scale', String(state.scale));
    return state.scale;
  }

  function ensureViewportStyle(viewportWidth) {
    let style = document.getElementById(STYLE_ID);
    if (!style) {
      style = document.createElement('style');
      style.id = STYLE_ID;
      document.head.appendChild(style);
    }
    style.textContent = `
      html {
        width: ${viewportWidth}px !important;
        max-width: ${viewportWidth}px !important;
        margin: 0 !important;
        transform-origin: top left;
        transform: scale(var(--hts-scale, 1));
        box-sizing: border-box;
      }
      body {
        width: ${viewportWidth}px !important;
        max-width: ${viewportWidth}px !important;
        margin: 0 !important;
        box-sizing: border-box;
      }
      #${OVERLAY_ID} {
        position: absolute;
        left: 0;
        top: 0;
        width: ${viewportWidth}px;
        pointer-events: none;
        z-index: 2147483646;
      }
      #${OVERLAY_ID} .hts-split-line {
        position: absolute;
        left: 0;
        width: 100%;
        height: ${LINE_HIT_HEIGHT}px;
        transform: translateY(-${Math.floor(LINE_HIT_HEIGHT / 2)}px);
        cursor: ns-resize;
        pointer-events: auto;
        touch-action: none;
        background: transparent;
      }
      #${OVERLAY_ID} .hts-split-line::after {
        content: '';
        position: absolute;
        left: 0;
        right: 0;
        top: 50%;
        height: 4px;
        margin-top: -2px;
        background: #e11d48;
        box-shadow: 0 0 0 1px rgba(255,255,255,0.9);
        pointer-events: none;
      }
      #${OVERLAY_ID} .hts-split-line::before {
        content: attr(data-label);
        position: absolute;
        right: 8px;
        top: 2px;
        font: 11px/1.2 Segoe UI, sans-serif;
        color: #fff;
        background: #e11d48;
        padding: 2px 6px;
        border-radius: 3px;
        white-space: nowrap;
        pointer-events: none;
      }
      #${OVERLAY_ID} .hts-split-line.hts-dragging::after {
        height: 6px;
        margin-top: -3px;
        background: #be123c;
      }
      #${OVERLAY_ID} .hts-split-hint {
        position: fixed;
        top: 8px;
        left: 8px;
        font: 12px/1.3 Segoe UI, sans-serif;
        color: #fff;
        background: rgba(0,0,0,0.65);
        padding: 6px 8px;
        border-radius: 4px;
        pointer-events: none;
        z-index: 2147483647;
      }
      body.hts-dragging-split {
        user-select: none !important;
        cursor: ns-resize !important;
      }
    `;
  }

  function pointerToDocY(ev) {
    const rect = document.documentElement.getBoundingClientRect();
    const scale = state.scale > 0 ? state.scale : 1;
    return (ev.clientY - rect.top) / scale + window.scrollY;
  }

  function ensureOverlay(viewportWidth, totalHeight) {
    if (getComputedStyle(document.body).position === 'static') {
      document.body.style.position = 'relative';
    }

    let overlay = document.getElementById(OVERLAY_ID);
    if (!overlay) {
      overlay = document.createElement('div');
      overlay.id = OVERLAY_ID;
      document.body.appendChild(overlay);
    }

    overlay.style.height = totalHeight + 'px';
    overlay.style.width = viewportWidth + 'px';
    return overlay;
  }

  function postSplits(splitYs) {
    try {
      window.chrome?.webview?.postMessage({
        type: 'splits',
        ys: splitYs,
        totalHeight: state.totalHeight
      });
    } catch (_) { /* preview outside WebView2 */ }
  }

  function clampSplits(splitYs, totalHeight) {
    const sorted = [...splitYs].sort((a, b) => a - b);
    const out = [];
    let prev = 0;
    for (const raw of sorted) {
      const minY = prev + MIN_GAP;
      const maxY = totalHeight - MIN_GAP;
      if (minY > maxY) break;
      const y = Math.max(minY, Math.min(maxY, Math.round(raw)));
      if (y > prev) {
        out.push(y);
        prev = y;
      }
    }
    return out;
  }

  function clampSingleSplit(index, splitYs, totalHeight) {
    const next = [...splitYs];
    const minY = index === 0 ? MIN_GAP : next[index - 1] + MIN_GAP;
    const maxY = index === next.length - 1
      ? totalHeight - MIN_GAP
      : next[index + 1] - MIN_GAP;
    next[index] = Math.round(Math.max(minY, Math.min(maxY, next[index])));
    return next;
  }

  function updateHint(hint, splitYs) {
    const scalePct = Math.round(state.scale * 100);
    const scaleNote = state.scale < 0.999 ? ` · preview ${scalePct}%` : '';
    hint.textContent = splitYs.length
      ? `${splitYs.length + 1} slides — drag red lines (${state.viewportWidth}px layout${scaleNote})`
      : `1 slide — content fits one page (${state.viewportWidth}px layout${scaleNote})`;
  }

  function setLineY(line, y) {
    line.style.top = y + 'px';
  }

  function renderLines(overlay, splitYs) {
    overlay.innerHTML = '';

    const hint = document.createElement('div');
    hint.className = 'hts-split-hint';
    hint.id = 'hts-split-hint';
    overlay.appendChild(hint);
    updateHint(hint, splitYs);

    splitYs.forEach((y, index) => {
      const line = document.createElement('div');
      line.className = 'hts-split-line';
      line.dataset.index = String(index);
      line.setAttribute('data-label', `Slide ${index + 1} | ${index + 2} @ ${y}px`);
      setLineY(line, y);

      line.addEventListener('pointerdown', (e) => {
        if (e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();

        state.dragging = true;
        document.body.classList.add('hts-dragging-split');
        line.classList.add('hts-dragging');

        const startDocY = pointerToDocY(e);
        const startSplitY = state.splitYs[index];
        const pointerId = e.pointerId;
        line.setPointerCapture(pointerId);

        const onMove = (ev) => {
          if (ev.pointerId !== pointerId) return;
          ev.preventDefault();
          const delta = pointerToDocY(ev) - startDocY;
          const tentative = state.splitYs.map((val, i) => i === index ? startSplitY + delta : val);
          state.splitYs = clampSingleSplit(index, tentative, state.totalHeight);
          setLineY(line, state.splitYs[index]);
          line.setAttribute('data-label', `Slide ${index + 1} | ${index + 2} @ ${state.splitYs[index]}px`);
          updateHint(hint, state.splitYs);
        };

        const onEnd = (ev) => {
          if (ev.pointerId !== pointerId) return;
          state.dragging = false;
          document.body.classList.remove('hts-dragging-split');
          line.classList.remove('hts-dragging');
          try { line.releasePointerCapture(pointerId); } catch (_) { }
          line.removeEventListener('pointermove', onMove);
          line.removeEventListener('pointerup', onEnd);
          line.removeEventListener('pointercancel', onEnd);
          state.splitYs = clampSplits(state.splitYs, state.totalHeight);
          syncLinePositions(overlay, state.splitYs);
          postSplits(state.splitYs);
        };

        line.addEventListener('pointermove', onMove);
        line.addEventListener('pointerup', onEnd);
        line.addEventListener('pointercancel', onEnd);
      });

      overlay.appendChild(line);
    });
  }

  function syncLinePositions(overlay, splitYs) {
    const lines = overlay.querySelectorAll('.hts-split-line');
    lines.forEach((line, index) => {
      if (index < splitYs.length) {
        setLineY(line, splitYs[index]);
        line.setAttribute('data-label', `Slide ${index + 1} | ${index + 2} @ ${splitYs[index]}px`);
      }
    });
    const hint = overlay.querySelector('#hts-split-hint');
    if (hint) updateHint(hint, splitYs);
  }

  function refreshOverlay(notify) {
    if (state.dragging) return;

    updateScale();
    state.totalHeight = getTotalHeight();
    const overlay = ensureOverlay(state.viewportWidth, state.totalHeight);
    state.splitYs = clampSplits(state.splitYs, state.totalHeight);
    renderLines(overlay, state.splitYs);
    if (notify) postSplits(state.splitYs);
  }

  window.__htsInstallSplitOverlay = function (config) {
    state.viewportWidth = config.viewportWidth || 1280;
    state.slideHeight = config.slideHeight || 720;
    state.splitYs = Array.isArray(config.splitYs) && config.splitYs.length
      ? clampSplits(config.splitYs, getTotalHeight())
      : computeDefaultSplits(getTotalHeight(), state.slideHeight);

    ensureViewportStyle(state.viewportWidth);
    updateScale();
    refreshOverlay(true);

    if (!window.__htsSplitResizeBound) {
      window.__htsSplitResizeBound = true;
      window.addEventListener('resize', () => {
        updateScale();
        if (!state.dragging) {
          const hint = document.querySelector('#hts-split-hint');
          if (hint) updateHint(hint, state.splitYs);
        }
      });
    }
  };

  window.__htsGetSplits = function () {
    return [...state.splitYs];
  };

  window.__htsGetTotalHeight = function () {
    return state.totalHeight > 0 ? state.totalHeight : getTotalHeight();
  };

  window.__htsResetSplits = function () {
    state.splitYs = computeDefaultSplits(getTotalHeight(), state.slideHeight);
    refreshOverlay(true);
    return [...state.splitYs];
  };

  window.__htsRefreshScale = function () {
    updateScale();
    const hint = document.querySelector('#hts-split-hint');
    if (hint) updateHint(hint, state.splitYs);
  };
})();
