# Show HN draft

A draft, not posted. Edit it in your own voice before you submit; Hacker News readers can tell.

## Title (pick one, keep it under 80 characters)

- Show HN: Hey Ganesh – a Windows tutor that draws the answer on your screen
- Show HN: Hold Ctrl+Alt, ask out loud, and it draws the explanation over what you're studying

## URL

https://github.com/PranavJha-3000/HeyGanesh

## First comment (post it yourself, right after submitting)

Hi HN. I built Hey Ganesh because after enough pages of reading, words stop meaning anything, and the fix is rarely
more text: it's a picture, drawn when you're confused, about the thing you're looking at. Chat assistants live in a
window next to your work. This one answers on top of it.

You hold Ctrl+Alt anywhere in Windows and ask out loud ("where's the Save button?", "why does a longer wrench loosen a
bolt?"). Depending on the question it outlines the element you mean, explains it in a spoken voice, or draws a 2D or 3D
diagram over your screen, one stroke per spoken sentence. It never clicks or types for you; a CI check bans the Win32
calls that could.

How it works, briefly:

- **Pointing:** it reads the window's UI Automation tree plus up to 40 lines of on-screen text from Windows' built-in
  OCR, and a model picks from a ranked list of candidates. A vision model is only the fallback.
- **Drawing:** the model writes a short program in a small drawing language. A sandboxed worker runs it, a mark check
  runs before each pen stroke, and the strokes are paced by the speech. Every taught turn is saved locally and can be
  replayed with no model call.
- **Models:** DeepSeek or your own Claude Code / ChatGPT (Codex) plan teaches; there's a Settings window in the tray so
  nothing needs a terminal. Voice is optional (Gnani). Keys live in Windows Credential Manager.

Install (Windows, per-user, no admin):

    irm https://raw.githubusercontent.com/PranavJha-3000/HeyGanesh/main/installer/install-online.ps1 | iex

Things I'd rather you hear from me: it's Windows only, the app isn't code-signed yet (SmartScreen will warn), finding
things on screen needs a TypeSafe key as well as a teaching model, English and Hindi are the only languages I've
configured, and I haven't run the Codex path live yet. The drawn lessons can be wrong: that's the thing I most want
feedback on. If one misleads you, I'd like to see the question and the picture.

MIT licensed. Happy to answer anything about the drawing sandbox or the UIA-first pointing.

## Before you post

- [ ] Install it once on a machine that isn't yours (a friend's, or a fresh Windows user). Not done yet.
- [ ] Check CI is green on `main` and the latest release downloads.
- [ ] Post on a weekday morning (US time) and stay at the keyboard for the first two hours: the early replies decide
      the ranking and the tone of the thread.
- [ ] Don't ask anyone for upvotes (it's against the rules and gets threads flagged).
- [ ] Have the demo GIF and a known-good lesson question ready to paste in replies.
