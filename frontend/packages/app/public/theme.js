// Runs before the first paint, from the document head: puts the theme and the character
// chosen in this browser on the root element, so a reload never shows the system's theme
// for an instant before the app starts. A file of its own, since the content security
// policy allows no inline script.
//
// It repeats what DisplaySettings does once the app runs (packages/util); a test there
// holds the two to the same keys and values.
(function () {
  var root = document.documentElement;
  try {
    var theme = localStorage.getItem('respawn.theme');
    if (theme === 'dark' || theme === 'light') root.setAttribute('data-theme', theme);
    var character = localStorage.getItem('respawn.character');
    if (character === 'pixel' || character === 'plain') {
      root.setAttribute('data-character', character);
    }
  } catch (refused) {
    // Storage refused (private browsing, a locked-down profile): the defaults apply.
  }
})();
