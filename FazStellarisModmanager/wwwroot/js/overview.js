// Measures an element for the tech overview's pan and zoom; all other logic is C#.
export function rect(el) {
    const r = el.getBoundingClientRect();
    return [r.left, r.top, r.width, r.height];
}
