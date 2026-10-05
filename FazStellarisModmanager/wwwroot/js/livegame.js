// Animates scoreboard rows (children with data-id and data-rank) from where they were last time to where they are
// now, and glows rows that moved up or down in rank. Positions are remembered between calls.
const last = new Map();

export function animate(board) {
    const rows = Array.from(board.querySelectorAll('[data-id]'));
    const now = new Map(rows.map(r => [r.dataset.id, { top: r.offsetTop, rank: Number(r.dataset.rank) }]));
    const first = last.size === 0;
    for (const r of rows) {
        const before = last.get(r.dataset.id);
        const after = now.get(r.dataset.id);
        if (first || !before) continue;
        const dy = before.top - after.top;
        if (dy !== 0) {
            r.style.transition = 'none';
            r.style.transform = `translateY(${dy}px)`;
        }
        r.classList.remove('moved-up', 'moved-down');
        if (after.rank < before.rank) r.classList.add('moved-up');
        else if (after.rank > before.rank) r.classList.add('moved-down');
    }
    requestAnimationFrame(() => requestAnimationFrame(() => {
        for (const r of rows) {
            r.style.transition = 'transform .7s cubic-bezier(.2,.8,.2,1)';
            r.style.transform = '';
        }
    }));
    setTimeout(() => rows.forEach(r => r.classList.remove('moved-up', 'moved-down')), 4000);
    last.clear();
    for (const [id, v] of now) last.set(id, v);
}
