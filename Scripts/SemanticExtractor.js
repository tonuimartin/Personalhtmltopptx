(function () {
  function rgbToHex(rgb) {
    if (!rgb || rgb === 'transparent') return null;
    const m = rgb.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
    if (!m) return rgb.startsWith('#') ? rgb : null;
    const h = n => parseInt(n, 10).toString(16).padStart(2, '0');
    return '#' + h(m[1]) + h(m[2]) + h(m[3]);
  }

  function captureStyles(el) {
    const s = getComputedStyle(el);
    return {
      backgroundColor: s.backgroundColor,
      color: s.color,
      fontFamily: s.fontFamily,
      fontSize: s.fontSize,
      borderRadius: s.borderRadius,
      boxShadow: s.boxShadow,
      textAlign: s.textAlign
    };
  }

  function inferType(el, classes) {
    const c = classes.toLowerCase();
    if (c.includes('grid') || c.includes('col-')) return 'card-grid';
    if (c.includes('card')) return 'card';
    if (c.includes('hero')) return 'hero';
    if (c.includes('timeline') || c.includes('workflow')) return 'timeline';
    if (c.includes('compare') || c.includes('table')) return 'comparison';
    if (el.querySelector('ul, ol')) return 'list';
    if (el.querySelector('input[type=checkbox]')) return 'checklist';
    const h = el.querySelector('h1,h2,h3');
    if (h && el.querySelectorAll('p').length <= 2) return 'hero';
    return 'content';
  }

  function textOf(el) {
    return (el.textContent || '').trim().replace(/\s+/g, ' ');
  }

  function sectionRoots() {
    const roots = Array.from(document.querySelectorAll('main > section, body > section, main > div, body > div, article'));
    if (roots.length === 0) roots.push(document.body);
    return roots.filter(el => {
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    });
  }

  function extractCards(section) {
    const cards = [];
    const candidates = section.querySelectorAll('[class*="card"], .grid > *, [class*="col-"] > *');
    candidates.forEach(card => {
      const title = card.querySelector('h1,h2,h3,h4,h5,h6');
      const body = card.querySelector('p');
      if (!title && !body) return;
      cards.push({
        title: title ? textOf(title) : null,
        body: body ? textOf(body) : null,
        classes: card.className || '',
        styles: captureStyles(card)
      });
    });
    return cards;
  }

  function extractIcons(section) {
    const icons = [];
    section.querySelectorAll('[class*="material-symbols"], [class*="material-icons"], [class*="fa-"], .bi-').forEach(el => {
      const name = (el.textContent || '').trim() || el.className;
      icons.push({
        name,
        classes: el.className || '',
        label: el.getAttribute('aria-label') || el.title || null
      });
    });
    return icons;
  }

  return function extractSemanticDocument(viewportWidth, viewportHeight, slideHeight) {
    const totalHeight = Math.max(document.body.scrollHeight, document.documentElement.scrollHeight, viewportHeight);
    const slideCount = Math.max(1, Math.ceil(totalHeight / slideHeight));
    const colorSet = new Set();
    const fontSet = new Set();

    document.querySelectorAll('*').forEach(el => {
      const s = getComputedStyle(el);
      const bg = rgbToHex(s.backgroundColor);
      const fg = rgbToHex(s.color);
      if (bg) colorSet.add(bg);
      if (fg) colorSet.add(fg);
      if (s.fontFamily) fontSet.add(s.fontFamily.split(',')[0].trim().replace(/['"]/g, ''));
    });

    const sections = sectionRoots().map((el, idx) => {
      const rect = el.getBoundingClientRect();
      const slideIndex = Math.min(slideCount - 1, Math.floor(rect.top / slideHeight));
      const classes = el.className && typeof el.className === 'string' ? el.className : '';
      const heading = el.querySelector('h1,h2,h3,h4');
      const sub = el.querySelector('h2,h3,h4,h5');
      const paragraphs = Array.from(el.querySelectorAll(':scope > p, p')).map(p => textOf(p)).filter(Boolean).slice(0, 12);
      const listItems = [];
      el.querySelectorAll('li').forEach(li => {
        const cb = li.querySelector('input[type=checkbox]');
        listItems.push({
          text: textOf(li),
          isCheckbox: !!cb,
          checked: cb ? cb.checked : false
        });
      });
      el.querySelectorAll('label').forEach(lab => {
        const cb = lab.querySelector('input[type=checkbox]');
        if (cb) listItems.push({ text: textOf(lab).replace(/\s+/g, ' '), isCheckbox: true, checked: cb.checked });
      });

      const images = Array.from(el.querySelectorAll('img')).map(img => ({
        src: img.currentSrc || img.src || '',
        alt: img.alt || ''
      }));

      return {
        id: el.id || 'section-' + idx,
        tag: el.tagName.toLowerCase(),
        classes,
        inferredType: inferType(el, classes),
        heading: heading ? textOf(heading) : null,
        subheading: sub && sub !== heading ? textOf(sub) : null,
        paragraphs,
        listItems,
        cards: extractCards(el),
        images,
        icons: extractIcons(el),
        styles: captureStyles(el),
        slideIndex
      };
    });

    return {
      pageTitle: document.title || '',
      viewportWidth,
      viewportHeight,
      estimatedSlideCount: slideCount,
      colorTokens: Array.from(colorSet).slice(0, 24),
      fontFamilies: Array.from(fontSet).slice(0, 12),
      sections
    };
  };
})();
