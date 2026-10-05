// Pointer-based reordering for the mod list. HTML5 drag and drop is unreliable inside WebView2,
// and this keeps the whole drag in the browser: .NET is only called once, on drop (MoveRow(from, to)).
// Rows are the list's li.mrow children with data-index = their real index in the list.
export function attach(list, dotnet) {
    let drag = null;
    const rows = () => Array.from(list.querySelectorAll(':scope > li.mrow'));

    function onDown(e) {
        finish(null, false); // a press that ended outside the list never got its pointerup
        if (e.button !== 0) return;
        const row = e.target.closest('li.mrow');
        if (!row || row.parentElement !== list || e.target.closest('input, button, a, select, textarea, label')) return;
        drag = { row, from: Number(row.dataset.index), pointer: e.pointerId, startY: e.clientY, y: e.clientY, active: false, target: null, frame: 0 };
    }

    function onMove(e) {
        if (!drag || e.pointerId !== drag.pointer) return;
        drag.y = e.clientY;
        if (!drag.active) {
            if (Math.abs(drag.y - drag.startY) < 5) return;
            drag.active = true;
            try { list.setPointerCapture(e.pointerId); } catch { }
            drag.row.classList.add('dragging');
            document.body.classList.add('reordering');
            drag.frame = requestAnimationFrame(tick);
        }
        e.preventDefault();
        mark();
    }

    // Auto-scrolls while the pointer is near the top or bottom edge of the list.
    function tick() {
        if (!drag || !drag.active) return;
        const box = list.getBoundingClientRect();
        const edge = 48;
        let speed = 0;
        if (drag.y < box.top + edge) speed = -Math.ceil((box.top + edge - drag.y) / 4);
        else if (drag.y > box.bottom - edge) speed = Math.ceil((drag.y - (box.bottom - edge)) / 4);
        if (speed !== 0) {
            list.scrollTop += speed;
            mark();
        }
        drag.frame = requestAnimationFrame(tick);
    }

    function mark() {
        const all = rows();
        if (all.length === 0) return;
        let target = null;
        for (const r of all) {
            const b = r.getBoundingClientRect();
            if (drag.y < b.bottom) { target = r; break; }
        }
        target ??= all[all.length - 1];
        if (drag.target && drag.target !== target) drag.target.classList.remove('drop-above', 'drop-below');
        drag.target = target;
        if (target === drag.row) return;
        const to = Number(target.dataset.index);
        target.classList.toggle('drop-below', drag.from < to);
        target.classList.toggle('drop-above', drag.from > to);
    }

    function finish(e, commit) {
        if (!drag || (e && e.pointerId !== drag.pointer)) return;
        const d = drag;
        drag = null;
        cancelAnimationFrame(d.frame);
        try { list.releasePointerCapture(d.pointer); } catch { }
        d.row.classList.remove('dragging');
        d.target?.classList.remove('drop-above', 'drop-below');
        document.body.classList.remove('reordering');
        if (commit && d.active && d.target && d.target !== d.row) {
            dotnet.invokeMethodAsync('MoveRow', d.from, Number(d.target.dataset.index));
        }
    }

    const onUp = e => finish(e, true);
    const onCancel = e => finish(e, false);
    const onKey = e => { if (e.key === 'Escape') finish(null, false); };

    list.addEventListener('pointerdown', onDown);
    list.addEventListener('pointermove', onMove);
    list.addEventListener('pointerup', onUp);
    list.addEventListener('pointercancel', onCancel);
    window.addEventListener('keydown', onKey);

    return {
        dispose() {
            finish(null, false);
            list.removeEventListener('pointerdown', onDown);
            list.removeEventListener('pointermove', onMove);
            list.removeEventListener('pointerup', onUp);
            list.removeEventListener('pointercancel', onCancel);
            window.removeEventListener('keydown', onKey);
        }
    };
}
