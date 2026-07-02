(function () {
  const ICON_FONT_FAMILIES = [
    'material symbols', 'material symbols outlined', 'material icons',
    'fontawesome', 'font awesome', 'fa-solid', 'fa-regular',
    'bootstrap-icons', 'remixicon', 'feather', 'octicons'
  ];

  const ICON_CLASS_PATTERNS = [
    'material-symbols', 'material-icons', 'fa-', 'fas ', 'far ', 'fab ',
    'bi-', 'ri-', 'icon-', 'glyphicon'
  ];

  function isIconFont(el, style) {
    const fam = (style.fontFamily || '').toLowerCase();
    if (ICON_FONT_FAMILIES.some(f => fam.includes(f))) return true;
    const cls = (el.className && typeof el.className === 'string') ? el.className.toLowerCase() : '';
    return ICON_CLASS_PATTERNS.some(p => cls.includes(p));
  }

  function isHidden(el, style, rect) {
    if (!el || el.nodeType !== 1) return true;
    const tag = el.tagName.toLowerCase();
    if (tag === 'script' || tag === 'style' || tag === 'head' || tag === 'meta' || tag === 'link' || tag === 'noscript') return true;
    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return true;
    if (rect.width <= 0 || rect.height <= 0) return true;
    return false;
  }

  function getDirectText(el) {
    let t = '';
    for (const n of el.childNodes) {
      if (n.nodeType === 3) t += n.textContent;
    }
    return t.trim();
  }

  function getLineRects(el) {
    const rects = [];
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        if (!node.textContent || !node.textContent.trim()) return NodeFilter.FILTER_REJECT;
        return NodeFilter.FILTER_ACCEPT;
      }
    });
    let node;
    while ((node = walker.nextNode())) {
      const range = document.createRange();
      range.selectNodeContents(node);
      for (const r of range.getClientRects()) {
        if (r.width > 0 && r.height > 0) {
          rects.push({ x: r.x, y: r.y, width: r.width, height: r.height, text: node.textContent });
        }
      }
    }
    return rects;
  }

  function pseudoInfo(el, pseudo) {
    const s = getComputedStyle(el, pseudo);
    const content = s.content;
    if (!content || content === 'none' || content === 'normal' || content === '""' || content === "''") return null;
    return {
      pseudo,
      content,
      rect: el.getBoundingClientRect(),
      style: captureStyle(s)
    };
  }

  function captureStyle(style) {
    return {
      backgroundColor: style.backgroundColor,
      backgroundImage: style.backgroundImage,
      color: style.color,
      fontFamily: style.fontFamily,
      fontSize: style.fontSize,
      fontWeight: style.fontWeight,
      lineHeight: style.lineHeight,
      letterSpacing: style.letterSpacing,
      textAlign: style.textAlign,
      borderRadius: style.borderRadius,
      borderColor: style.borderColor,
      borderWidth: style.borderWidth,
      borderStyle: style.borderStyle,
      boxShadow: style.boxShadow,
      opacity: style.opacity,
      zIndex: style.zIndex,
      position: style.position,
      filter: style.filter,
      backdropFilter: style.backdropFilter,
      clipPath: style.clipPath,
      textDecoration: style.textDecoration,
      whiteSpace: style.whiteSpace
    };
  }

  function parseZ(z) {
    if (z === 'auto') return 0;
    const n = parseInt(z, 10);
    return isNaN(n) ? 0 : n;
  }

  function walkElements(root, slideTop, slideBottom, list, orderRef) {
    const tree = root.querySelectorAll('*');
    const elements = root === document.body ? [document.body, ...tree] : [root, ...tree];

    for (const el of elements) {
      if (el.nodeType !== 1) continue;
      const style = getComputedStyle(el);
      const rect = el.getBoundingClientRect();
      if (isHidden(el, style, rect)) continue;

      const bottom = rect.bottom;
      const top = rect.top;
      if (bottom < slideTop || top > slideBottom) continue;

      const tag = el.tagName.toLowerCase();
      const id = el.id || '';
      const classes = el.className && typeof el.className === 'string' ? el.className : '';
      const paintOrder = orderRef.value++;

      const item = {
        htsId: orderRef.value,
        path: buildPath(el),
        tag,
        id,
        classes,
        paintOrder,
        zIndex: parseZ(style.zIndex),
        rect: { x: rect.x, y: rect.y - slideTop, width: rect.width, height: rect.height },
        absoluteRect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
        style: captureStyle(style),
        text: getDirectText(el),
        lineRects: [],
        flags: {
          isImage: tag === 'img',
          isInlineSvg: tag === 'svg',
          isIconFont: isIconFont(el, style),
          isCheckbox: tag === 'input' && (el.type === 'checkbox' || el.type === 'radio'),
          isFormControl: tag === 'input' || tag === 'select' || tag === 'textarea' || tag === 'button',
          hasFilter: !!(style.filter && style.filter !== 'none'),
          hasBackdropFilter: !!(style.backdropFilter && style.backdropFilter !== 'none'),
          hasClipPath: !!(style.clipPath && style.clipPath !== 'none'),
          needsRasterize: false,
          rasterizeReason: null
        },
        image: null,
        pseudoElements: []
      };

      if (tag === 'img') {
        const img = el;
        item.image = {
          src: img.currentSrc || img.src || '',
          naturalWidth: img.naturalWidth || 0,
          naturalHeight: img.naturalHeight || 0
        };
      }

      const isTextLeaf = item.text.length > 0 && el.children.length === 0;
      if (isTextLeaf || (item.text.length > 0 && ['span', 'p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'a', 'label', 'li', 'td', 'th'].includes(tag))) {
        const lines = getLineRects(el);
        item.lineRects = lines.map(l => ({
          x: l.x,
          y: l.y - slideTop,
          width: l.width,
          height: l.height,
          text: (l.text || '').trim()
        })).filter(l => l.width > 0 && l.height > 0);
      }

      if (tag === 'input' && el.type === 'checkbox') {
        item.flags.checked = el.checked;
      }

      const before = pseudoInfo(el, '::before');
      const after = pseudoInfo(el, '::after');
      if (before) {
        before.rect = { ...before.rect, y: before.rect.y - slideTop };
        item.pseudoElements.push(before);
      }
      if (after) {
        after.rect = { ...after.rect, y: after.rect.y - slideTop };
        item.pseudoElements.push(after);
      }

      if (item.flags.isIconFont) {
        item.flags.needsRasterize = true;
        item.flags.rasterizeReason = 'Icon font glyph';
      } else if (item.flags.isInlineSvg) {
        item.flags.needsRasterize = true;
        item.flags.rasterizeReason = 'Inline SVG';
      } else if (item.flags.hasBackdropFilter) {
        item.flags.needsRasterize = true;
        item.flags.rasterizeReason = 'backdrop-filter';
      } else if (item.flags.hasFilter) {
        item.flags.needsRasterize = true;
        item.flags.rasterizeReason = 'CSS filter';
      } else if (item.flags.hasClipPath) {
        item.flags.needsRasterize = true;
        item.flags.rasterizeReason = 'clip-path';
      }

      list.push(item);
      try { el.setAttribute('data-hts-id', String(item.htsId)); } catch (_) {}
    }
  }

  function buildPath(el) {
    const parts = [];
    let cur = el;
    while (cur && cur !== document.documentElement) {
      let seg = cur.tagName ? cur.tagName.toLowerCase() : '';
      if (cur.id) seg += '#' + cur.id;
      else if (cur.className && typeof cur.className === 'string' && cur.className.trim()) {
        seg += '.' + cur.className.trim().split(/\s+/).slice(0, 2).join('.');
      }
      parts.unshift(seg);
      cur = cur.parentElement;
    }
    return parts.join(' > ');
  }

  return function extractDomForSlides(viewportWidth, viewportHeight, slideHeight, splitYs) {
    const totalHeight = Math.max(document.body.scrollHeight, document.documentElement.scrollHeight, viewportHeight);

    function buildSlideRanges() {
      const tops = [0];
        if (splitYs !== null && splitYs !== undefined) {
        if (splitYs.length > 0) {
          const sorted = [...splitYs]
            .filter(y => Number.isFinite(y) && y > 0)
            .sort((a, b) => a - b);
          for (const y of sorted) {
            const clamped = Math.min(Math.round(y), totalHeight);
            if (tops[tops.length - 1] < clamped && clamped < totalHeight)
              tops.push(clamped);
          }
        }
      } else {
        for (let y = slideHeight; y < totalHeight; y += slideHeight) tops.push(y);
      }

      const ranges = [];
      for (let i = 0; i < tops.length; i++) {
        const top = tops[i];
        const bottom = i < tops.length - 1 ? tops[i + 1] : totalHeight;
        const height = bottom - top;
        if (height > 0) ranges.push({ top, height });
      }
      if (ranges.length === 0) {
        ranges.push({ top: 0, height: Math.max(totalHeight, slideHeight) });
      }
      return ranges;
    }

    const ranges = buildSlideRanges();
    const slides = [];

    for (let i = 0; i < ranges.length; i++) {
      const slideTop = ranges[i].top;
      const slideHeightActual = ranges[i].height;
      const slideBottom = slideTop + slideHeightActual;
      const elements = [];
      const orderRef = { value: 0 };
      walkElements(document.body, slideTop, slideBottom, elements, orderRef);

      elements.sort((a, b) => {
        if (a.zIndex !== b.zIndex) return a.zIndex - b.zIndex;
        return a.paintOrder - b.paintOrder;
      });

      slides.push({
        slideIndex: i,
        slideTop,
        slideHeight: slideHeightActual,
        viewportWidth,
        viewportHeight,
        elements
      });
    }

    return {
      totalHeight,
      slideCount: slides.length,
      viewportWidth,
      viewportHeight,
      slideHeight,
      slides
    };
  };
})();
