// The only script: Replay restarts the hero drawing. Removing the class, forcing a reflow and adding it
// back is what makes the CSS animations start again from the first stroke.
const stage = document.querySelector('.stage');
document.querySelector('.replay')?.addEventListener('click', () => {
  stage.classList.remove('play');
  void stage.offsetWidth;
  stage.classList.add('play');
});
