// Replay restarts the hero drawing: removing the class, forcing a reflow and adding it back
// makes the CSS animations start again from the first stroke.
const stage = document.querySelector('.stage');
document.querySelector('.replay')?.addEventListener('click', () => {
  stage.classList.remove('play');
  void stage.offsetWidth;
  stage.classList.add('play');
});

// Sections settle into place as they scroll in. The "js" class gates the hidden start state in CSS,
// so without this script (or without IntersectionObserver) everything simply shows.
if ('IntersectionObserver' in window) {
  document.documentElement.classList.add('js');
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
  }, { threshold: 0.12 });
  document.querySelectorAll('[data-reveal]').forEach((el) => io.observe(el));
}
