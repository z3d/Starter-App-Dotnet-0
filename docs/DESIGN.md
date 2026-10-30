# StarterApp UI — the flock's design system

**Status:** Adopted 31 October 2026, from the shape ZSalaryPackage (NovatedLeasing) arrived at · **Source of truth:** `web/src/index.css` (tokens), `web/src/components/ui/` (primitives). A derived product keeps the shape, sets its own two accents in `index.css`, draws its own mark in `app-shell.tsx`, and writes its screens with these primitives.

## Direction

**The figure is the picture.** People open these products for figures: a balance, what leaves a pay, who is in the room, what is owed. Each screen's one figure that matters is set large on a dark statement band, lit from one corner by the accent and the other by the highlight, and the parts it is made of are drawn as a bar with a legend, never only listed. Around that the system stays warm paper: a faint grain on the canvas, a soft glow from each accent at the top of the page, a header band on every screen, cards just off the paper, an editorial serif for titles and figures and a quiet humanist sans for everything else.

Seven rules:

1. **One figure per screen is the hero.** A screen with a figure a person came for sets it on a `Statement` with `HeroFigure`; the figures that qualify it sit on the same band, not in equal boxes beside it. A screen of counts sets them on one `FigureStrip`, not a row of tiles.
2. **A figure made of parts shows its parts.** A `SplitBar` with each part named and its amount in the legend. Two figures in different money are never drawn as bars against each other.
3. **Two accents, one job each.** The *accent* marks what to act on and the first part of a split; the *highlight* marks where you are (the active rail item, the current step) and the second part. The statement band is the one place either sits at full strength as a surface. The negative red sits apart from the accent, so a refusal never reads as the brand.
4. **Numbers are data.** Tabular, slashed-zero numerals everywhere money, rates or counts appear (`.tnum`). Money shows its currency's scale and never a rounded cent.
5. **Colour never carries meaning alone.** A status or a split's part always pairs colour with a label and, where space allows, an icon; a stopped step says why in words.
6. **A staff screen is about the next decision.** A queue row names who, the next step in the accent and why, and how long it has waited (`QueueRow`, `Waited`), oldest first; a list of records opens on its stages with a count each (`StageFlow`).
7. **An estimate says so.** A figure the product worked out from tables or assumptions is labelled an estimate where it appears.

## Tokens

Defined once as CSS custom properties on `:root`, overridden for dark under `prefers-color-scheme: dark` (guarded by `:root:not([data-theme='light'])`) and again under `:root[data-theme='dark']` so an explicit toggle wins both ways. Exposed to Tailwind v4 through `@theme inline`.

| Group | Tokens | Notes |
|---|---|---|
| Surfaces | `canvas`, `surface`, `surface-raised`, `surface-sunken` | canvas is the page; surface for cards/tables/inputs; raised for popovers; sunken for table heads and wells |
| Text | `ink`, `ink-secondary`, `ink-tertiary` | three levels only |
| Lines | `line`, `line-strong` | hairlines; strong for dashed empty states and hover borders |
| Accents | `accent`, `accent-deep`, `accent-ink`, `accent-soft`; `highlight`, `highlight-ink`, `highlight-soft` | rule 3; `accent-deep` is the end of the primary button's gradient and the eyebrow's ink. A product sets these seven and nothing else changes |
| Glow | `glow-accent`, `glow-highlight`, `card-shadow` | the page's fixed background glow and the soft half of `shadow-card` |
| Sidebar | `rail`, `rail-ink`, `rail-ink-muted`, `rail-line`, `rail-active` | the dark rail is its own mini-palette so it works in both themes; `rail-lit` lights it from the accents |
| Semantic | `positive`, `caution`, `negative`, `info`, each with a `-soft` surface; `ai` for machine-made content | chips, callouts |
| Focus | `focus` | a ring, 2 px offset, on every focusable element |

Legacy shadcn-style aliases (`background`, `foreground`, `muted`, `border`, `primary`, `destructive`, `ring`) map onto the above so drop-in components keep working.

**Type.** Instrument Sans (UI, 14 px base at 1.5 line height), Instrument Serif (display: page titles at 2.5 rem on the header band, section titles at 1.25 rem, hero numbers at 4 to 5 rem), IBM Plex Mono (identifiers, references, keyboard hints). Loaded from Google Fonts with system fallbacks.

**Shape and depth.** Radii 6 / 8 / 12 / 16 px; cards and tables are 12 px. `shadow-card` (the `card` utility) is the default for anything on the page; `shadow-hairline` stays for small controls; `shadow-lift` only on hover of interactive cards; `shadow-pop` only under the command palette and dialogs.

**Motion.** 160 ms ease-out default; a page rises 4 px into place as it opens (`rise`, 320 ms), as a callout does; a split bar grows in from the left (`grow-bar`, 640 ms); skeleton shimmer 1.4 s. Everything collapses to 1 ms under `prefers-reduced-motion`.

**Texture.** A fixed grain (SVG fractal noise at 5 %) lies over the whole page, so the paper is not flat.

## Primitives (`web/src/components/ui/`)

| Primitive | Used for |
|---|---|
| `Button` — `primary` (ink), `accent` (the accent's gradient), `secondary` (card), `ghost`, `danger`, `link`; sizes sm/md/lg/icon | every action |
| `PageHeader` | the header band: a card washed from surface through the accent to the highlight, contour lines in its corner, a highlight-dotted eyebrow, the serif title at 2.5 rem, description, actions |
| `Card` | grouping; `interactive` adds the lift on hover |
| `Statement`, `HeroFigure` (`figures.tsx`) | the dark band for a screen's one figure: the figure in the serif at 4–5 rem, a highlight eyebrow, highlight contour lines along its foot |
| `Figure`, `FigureStrip` (`figures.tsx`) | a labelled figure with an icon; a strip sets several on one card divided by hairlines, `dark` sets one on a statement, `small` sets a value in words a size down |
| `StatTile` | one figure on a card; prefer a `FigureStrip` for several and a `Statement` for the hero |
| `SplitBar` (`figures.tsx`) | a whole and its parts: a rounded bar that grows in, and a legend naming each part with its amount; tones `accent`, `deep`, `highlight`, `positive`, `caution`, `info`, `rest` |
| `StageFlow` (`queue.tsx`) | a list's stages in order, a count each, an urgent dot on a stage with work in it; pressing one filters the list to it |
| `Queue`, `QueueRow`, `Waited` (`queue.tsx`) | a queue of items waiting on someone: who, a detail line, the next step and its reason, an amount, and how long it has waited in a tone that turns slow and late |
| `Steps` | a record's way through its stages; done in the accent with a tick, current in the highlight, a stopped step in negative with its reason as the label |
| `Avatar` | a person's initials on one of five soft hues, picked from the name |
| `DataTable`, `Th`, `Td`, `Tr` | dense lists; sunken uppercase header row, hairline row dividers, right-aligned numerals, an accent wash on hover |
| `Chip` — neutral/positive/caution/negative/info/ai/accent | statuses and treatments; a dot in the tone before the label, never wrapped |
| `Callout` | inline guidance; a tone's icon and a 3 px rule on its left |
| `EmptyState` | dashed well with an accent glow, an `icon` in a raised circle, serif headline, one call to action; `compact` is one line for a section with nothing in it yet (`done` turns its icon green) |
| `Skeleton`, `TableSkeleton`, `ShowMore` | loading shaped like the content; a list that comes a page at a time |
| `Field`, `Fieldset`, `Input`, `Select`, `Textarea`, `Checkbox` | forms; hint below, error replaces hint, `aria-invalid` border |
| `Kbd` | keyboard hints |

## Composite components

- **App shell** (`app-shell.tsx`): 240 px dark rail lit from its corners, the mark, ⌘K, the primary nav (the active item carries a highlight bar and a highlight icon), a note card, and the user card; on mobile a top bar plus a bottom tab bar. The page content rises into place on each route. Content is capped at 72 rem.
- **Command palette** (`command-palette.tsx`): cmdk over a Radix dialog; groups *Go to* and *Account*; a product adds *Create* and record search.

## Accessibility

WCAG 2.2 AA is the bar: every text-on-surface pair in both themes clears 4.5:1; focus is always visible; touch targets are at least 40 px on mobile; icons are `aria-hidden` when a label is present; forms use real `<label>` wrapping and `role="alert"` errors; reduced motion is honoured.

## What this design avoids

- No row of three or four equal boxes for a screen's figures: one hero on a statement, or one strip.
- No photograph, stock image or illustration that stands in for content.
- No chart that compares money of different kinds as if it were the same.
- No third accent: the accent is for what to act on, the highlight for where you are.
- No status or part carried by colour alone.
- No proportional numerals where money or counts appear.
- No estimate shown as a fact.
- No raw colour in a component: a token first.

## How a derived product takes it

Set `accent`, `accent-deep`, `accent-ink`, `accent-soft`, `highlight`, `highlight-ink`, `highlight-soft` and the two glows in `index.css` for light and dark; draw the mark; name the product in the shell. Add a token before adding a raw colour. Add a primitive before styling a one-off. A new screen starts with `PageHeader`; then a `Statement` when it has a figure a person came for, and either `DataTable` or a `1fr + 16rem` grid. Run `web/e2e/screenshots.mjs` against the dev stack to see every screen in light, dark and mobile before merging a visual change.
