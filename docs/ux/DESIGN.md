---
name: DisputeDesk
description: Plain-language card-dispute portal for customers of a South African retail bank, in a friendly, high-contrast retail-banking style. shadcn/ui on Tailwind; this file is the brand layer and the single source of the theme tokens.
status: approved
updated: 2026-10-26
companions:
  - EXPERIENCE.md
  - mockups/tokens.css
colors:
  # Token names match shadcn/ui CSS variables so the theme file drops straight in.
  # Light theme (default). Ratios are WCAG 2.2 contrast against the surface named.
  background: '#F4F5F7'          # page canvas, light grey
  foreground: '#1E2430'          # body text, 14.26:1 on background, 15.55:1 on card
  card: '#FFFFFF'                # surfaces: cards, rows, dialogs
  card-foreground: '#1E2430'
  muted: '#EEF0F3'               # quiet fills: skeletons, quote blocks, step numbers, desktop nav track
  muted-foreground: '#586070'    # secondary text, 6.32:1 on card, 5.79:1 on background
  border: '#E1E4EA'              # dividers (decorative, no contrast requirement)
  input: '#848D9D'               # field and control borders, 3.35:1 on card, 3.07:1 on background
  heading: '#14287A'             # deep navy: display headline, page titles, hero amounts. 13.01:1 on card
  primary: '#2B57C9'             # THE accent: cobalt. 6.35:1 on card
  primary-foreground: '#FFFFFF'  # 6.35:1 on primary
  primary-hover: '#2247AD'       # white on it 8.16:1
  primary-pressed: '#1B3A8F'     # white on it 10.25:1
  primary-subtle: '#E8EEFB'      # selected reason fill, timeline current-dot halo, start-page stage; accent on it 5.46:1
  ring: '#2B57C9'                # focus ring, 5.82:1 on background
  destructive: '#B42318'         # errors and the withdraw confirm only, 6.57:1 on card
  destructive-foreground: '#FFFFFF'
  # Status tones (semantic, not brand, never the accent). Tint bg + dark text + label + icon.
  status-info-bg: '#E2F2F5'        # Submitted, Under review (teal, kept apart from cobalt)
  status-info-fg: '#0B4F5E'        # 7.96:1
  status-priority-bg: '#FFF1D6'    # Under review (prioritised)
  status-priority-fg: '#7A4300'    # 7.15:1
  status-success-bg: '#E3F4EA'     # Resolved, Upheld
  status-success-fg: '#156038'     # 6.66:1
  status-danger-bg: '#FDECEA'      # Resolved, Rejected; error banners
  status-danger-fg: '#9A1C13'      # 7.20:1
  status-neutral-bg: '#EEF0F3'     # Withdrawn; not-disputable hints
  status-neutral-fg: '#454D5C'     # 7.45:1
  # Dark theme (follows prefers-color-scheme). Surfaces are slate.
  background-dark: '#12171E'
  foreground-dark: '#EEF1F5'
  card-dark: '#1F2933'             # slate
  muted-dark: '#2A3441'
  muted-foreground-dark: '#A9B2C0'
  border-dark: '#323D4B'
  input-dark: '#7A8697'
  heading-dark: '#C9D6FF'
  primary-dark: '#8CAAF7'          # cobalt lightened for contrast on slate
  primary-foreground-dark: '#0B1838'
  primary-hover-dark: '#A9C0FA'
  primary-pressed-dark: '#C4D4FC'
  primary-subtle-dark: '#22355F'
  destructive-dark: '#FF8A7E'
  destructive-foreground-dark: '#2A0B08'
  status-info-bg-dark: '#103A43'
  status-info-fg-dark: '#A6E0EA'
  status-priority-bg-dark: '#3F2D0C'
  status-priority-fg-dark: '#F5CF8A'
  status-success-bg-dark: '#123A26'
  status-success-fg-dark: '#9BDDB8'
  status-danger-bg-dark: '#4A1A16'
  status-danger-fg-dark: '#FFB4AB'
  status-neutral-bg-dark: '#2A3441'
  status-neutral-fg-dark: '#C8CED8'
typography:
  # One family: Nunito Sans (SIL OFL 1.1), self-hosted WOFF2 (latin, 400/600/700/800) because the CSP is font-src 'self'.
  display:                       # start page only; never on app screens
    fontFamily: '"Nunito Sans", system-ui, -apple-system, "Segoe UI", Roboto, Arial, sans-serif'
    fontSize: 56px               # 36px below 768px; two lines at 1280px
    fontWeight: '800'
    lineHeight: 60px             # 1.1 on phones
    letterSpacing: -0.02em
    color: '{colors.heading}'
  title:                         # h1
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 32px
    fontWeight: '800'
    lineHeight: '1.2'
    letterSpacing: -0.01em
    color: '{colors.heading}'
  heading:                       # h2
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 24px
    fontWeight: '700'
    lineHeight: '1.3'
  subheading:                    # h3
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 20px
    fontWeight: '700'
    lineHeight: '1.3'
  body:
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 16px
    fontWeight: '400'
    lineHeight: '1.5'
  label:                         # small
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 14px
    fontWeight: '700'
    lineHeight: '1.4'
  caption:
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 13px
    fontWeight: '400'
    lineHeight: '1.4'
  amount:
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 16px
    fontWeight: '700'
    lineHeight: '1.3'
    note: 'font-variant-numeric: tabular-nums'
  amount-hero:
    fontFamily: '{typography.display.fontFamily}'
    fontSize: 32px
    fontWeight: '800'
    lineHeight: '1.1'
    color: '{colors.heading}'
    note: 'font-variant-numeric: tabular-nums'
  mono:
    fontFamily: 'ui-monospace, "SF Mono", Menlo, Consolas, monospace'
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.4'
rounded:
  # Radius follows hierarchy: rows none, list panels and fields md, cards and form surfaces lg, controls full.
  none: 0      # list rows (they sit on a panel, divided by hairlines)
  sm: 8px      # focus outline, mock notes
  md: 12px     # list panels, text fields, the reason group, OTP cells, notices, quote blocks
  lg: 24px     # the elevated summary card, form surfaces, empty states, dialogs, the hosted-login card
  full: 9999px # every button, status badges, the segmented nav, step numbers, timeline dots, skeleton bars, the wordmark dot
spacing:
  # 4-based, a subset of the Tailwind scale (Tailwind class in brackets).
  '1': 4px     # [1]
  '2': 8px     # [2]
  '3': 12px    # [3]
  '4': 16px    # [4]  phone gutter
  '6': 24px    # [6]  section gap; desktop gutter; card padding
  '8': 32px    # [8]  section gap on desktop
  '12': 48px   # [12] page top on desktop; button height
  '16': 64px   # [16] desktop section spacing, page foot
  tap-target: 44px
  content-max: 880px
  form-max: 560px
  page-max: 1120px   # start page only: two-column hero
  row-min: 64px      # list row minimum height
  amount-col: 9rem   # desktop list: amount column, right-aligned
  action-col: 14rem  # desktop list: Dispute link or status badge column
components:
  button-primary:
    background: '{colors.primary}'
    foreground: '{colors.primary-foreground}'
    hover: '{colors.primary-hover}'
    pressed: '{colors.primary-pressed}'
    radius: '{rounded.full}'
    height: '{spacing.12}'
  button-secondary:
    background: '{colors.card}'
    foreground: '{colors.primary}'
    border: '1.5px {colors.primary}'
    hover-background: '{colors.primary-subtle}'
    radius: '{rounded.full}'
    height: '{spacing.12}'
  button-destructive:
    background: '{colors.destructive}'
    foreground: '{colors.destructive-foreground}'
    radius: '{rounded.full}'
  segmented-nav:
    track: '{colors.muted}'      # desktop; the phone bar floats on {colors.card}
    item-foreground: '{colors.muted-foreground}'
    active-background: '{colors.primary}'
    active-foreground: '{colors.primary-foreground}'
    radius: '{rounded.full}'
  status-badge:
    radius: '{rounded.full}'
    typography: '{typography.label}'
    padding: '2px 10px'
  card:                          # elevated: one per screen, the dispute summary
    background: '{colors.card}'
    radius: '{rounded.lg}'
    padding: '{spacing.6}'
    shadow: '0 4px 16px rgba(20,40,122,0.08)'
  surface:                       # flat: forms, the review read-back, empty states
    background: '{colors.card}'
    border: '1px {colors.border}'
    radius: '{rounded.lg}'
    padding: '{spacing.6} {spacing.4}; {spacing.8} from 768px'
  list-panel:                    # holds rows; day or Open/Closed headings sit inside or above it
    background: '{colors.card}'
    border: '1px {colors.border}'
    radius: '{rounded.md}'
  list-row:
    radius: '{rounded.none}'
    divider: '1px {colors.border}'
    min-height: '{spacing.row-min}'
    padding: '{spacing.3} {spacing.4}'
  timeline-entry:
    rail: '2px {colors.border}'
    dot-earlier: '12px, 2px {colors.input} ring, {colors.card} fill'
    dot-current: '16px {colors.primary} fill, 4px {colors.primary-subtle} halo'
    current-text: '{colors.heading}, 800'
  status-card:                   # start page only: a sample dispute, aria-hidden, captioned
    background: '{colors.card}'
    radius: '{rounded.lg}'
    shadow: '0 12px 32px rgba(20,40,122,0.18)'
    stage: '{colors.primary-subtle}, {rounded.lg}'
  steps:                         # start page "How it works", a real sequence of three
    number: '40px {rounded.full}, {colors.muted} fill, {colors.heading} 20/800'
  footer:
    border-top: '1px {colors.border}'
    typography: '{typography.caption}'
    color: '{colors.muted-foreground}'
  problem-notice:
    background: '{colors.status-danger-bg}'
    foreground: '{colors.status-danger-fg}'
    radius: '{rounded.md}'
  otp-input:
    typography: '{typography.heading}'
    radius: '{rounded.md}'
    border: '{colors.input}'
---

## Brand & Style

DisputeDesk is the part of a bank's app a customer opens when something has gone wrong with their money. They arrive worried, often on a phone. The design's job is to lower the temperature and make the next step obvious: a light, uncluttered surface, plain words, one clear action per screen, and the status of every dispute readable at a glance.

The look is a friendly, high-contrast retail-banking style: big navy headlines, one vivid cobalt accent carrying every action, fully rounded pill buttons, soft white cards on a light-grey canvas and generous spacing. Copy is short, direct and warm. No real bank's colours, logos, imagery or typefaces are used. The identity is deliberately thin so it can be replaced: every visual decision here is a token, and all tokens live in one theme file (`frontend/src/theme/tokens.css`, mirrored today by [mockups/tokens.css](mockups/tokens.css)). Swapping the brand means editing that file and the hosted-login label policy generated from it, nothing else.

shadcn/ui is the component vocabulary. This file specifies only the brand layer on top of it.

## Colors

One accent, everything else neutral or semantic.

- **Cobalt `{colors.primary}` (#2B57C9; hover #2247AD, pressed #1B3A8F; dark #8CAAF7)** is the only brand colour and it means "an action is available here": filled primary buttons, secondary-button outlines and labels, links, the active nav segment, the focus ring, the selected reason card, the current timeline dot. It is never used for status or decoration. `{colors.primary-subtle}` (#E8EEFB) is its tint for selected fills and secondary-button hover.
- **Navy `{colors.heading}` (#14287A; dark #C9D6FF)** is a text colour only: the display headline, page titles and hero amounts. It gives the large type weight without adding a second accent.
- **Canvas `{colors.background}` (#F4F5F7)** is a light grey; **surfaces `{colors.card}`** are white; **quiet fills `{colors.muted}`** (#EEF0F3) for skeletons, quote blocks and the start page's step numbers; **dividers `{colors.border}`** (#E1E4EA).
- **Text `{colors.foreground}` (#1E2430)** is near-black; **secondary text `{colors.muted-foreground}` (#586070)** is mid grey.
- **Field borders `{colors.input}` (#848D9D)** meet the 3:1 non-text rule on both white and the canvas.
- **Dark theme** uses slate surfaces (`card-dark` #1F2933 on #12171E) and a lightened cobalt with dark labels on filled buttons. Dark slate is the surface for any dark panel; the light theme has none today.
- **No red accent.** Red appears only as `{colors.destructive}` (#B42318) for error text and the final Withdraw confirm, and as the danger status tone.
- **Status tones** are semantic, low-saturation tints with dark text, retuned so none reads as the accent: "info" is teal, not blue, so a Submitted badge never looks like a button.

| Customer-facing status | Tone | Icon |
|---|---|---|
| Submitted | info (teal) | paper-plane |
| Under review | info (teal) | magnifier |
| Under review (prioritised) | priority (amber) | magnifier with up-arrow |
| Resolved · Upheld | success (green) | check |
| Resolved · Rejected | danger (red) | cross |
| Withdrawn | neutral (grey) | arrow-back |

Colour is never the only signal: every badge carries its text label and an icon.

### Contrast (WCAG 2.2 AA), computed

| Pair | Light | Dark |
|---|---|---|
| Body text on canvas | 14.26:1 | 15.88:1 |
| Body text on card | 15.55:1 | 13.02:1 |
| Secondary text on card | 6.32:1 | 6.90:1 |
| Secondary text on canvas | 5.79:1 | 8.41:1 |
| Secondary text on muted fill (quote blocks, skeleton labels) | 5.54:1 | 5.89:1 |
| Navy headline on canvas / card | 11.93:1 / 13.01:1 | 12.46:1 / 10.22:1 |
| Accent text/link on card | 6.35:1 | 6.47:1 |
| Accent text/link on canvas | 5.82:1 | 7.89:1 |
| Accent on selected fill | 5.46:1 | 5.28:1 |
| Button label on accent | 6.35:1 | 7.66:1 |
| Button label on hover | 8.16:1 | 9.66:1 |
| Button label on pressed | 10.25:1 | 11.79:1 |
| Start page caption on its stage (foreground on primary-subtle) | 13.37:1 | 10.64:1 |
| Step number (navy on muted) | 11.39:1 | 8.73:1 |
| Footer text (secondary on canvas) | 5.79:1 | 8.41:1 |
| Timeline current dot on card (non-text, 3:1) | 6.35:1 | 6.47:1 |
| Timeline earlier dot ring on card (non-text, 3:1) | 3.35:1 | 3.99:1 |
| Selected reason ring on its fill (non-text, 3:1) | 5.46:1 | 5.28:1 |
| Focus ring on canvas (non-text, 3:1) | 5.82:1 | 7.89:1 |
| Field border on card / canvas (non-text, 3:1) | 3.35:1 / 3.07:1 | 3.99:1 / 4.87:1 |
| Error text on card / canvas | 6.57:1 / 6.03:1 | 6.46:1 / 7.87:1 |
| Destructive button label | 6.57:1 | 7.99:1 |
| Status badges (fg on bg) | info 7.96, priority 7.15, success 6.66, danger 7.20, neutral 7.45 | 8.47, 8.90, 8.09, 8.53, 7.97 |

Every text pair passes AA (4.5:1); badges, headings and body pass AAA (7:1) except success (6.66:1). The field border was darkened from #8A93A3 to #848D9D so it also clears 3:1 on the canvas (it was 2.84:1). The design pass of 26 Oct 2026 added no colour values; its new pairs are the last six rows above. The upcoming-step ring on the start page's sample card uses `{colors.border}` (1.27:1) on purpose: it is decorative, inside an `aria-hidden` sample, and the step is named in text.

## Typography

One family, **Nunito Sans** (SIL Open Font License 1.1), self-hosted as WOFF2 in the theme's `fonts/` folder (latin subset, weights 400, 600, 700, 800) with `font-display: swap`, because the nginx CSP allows fonts only from `'self'`. Its rounded terminals suit the friendly tone. Its default figures are already tabular (every digit has the same advance), so amounts line up; `font-variant-numeric: tabular-nums` stays on amounts to keep the fallback font aligned too. The system stack is the fallback while it loads and for the few glyphs outside the latin subset (the narrow no-break space in amounts, arrows); browsers substitute those per character. Sentence case everywhere: titles, buttons, nav, badges.

| Role | Token | Size / weight | Use |
|---|---|---|---|
| Display | `{typography.display}` | 56/60, 800, navy (36 on phones) | Start page headline only, balanced to two lines at 1280px |
| Title (h1) | `{typography.title}` | 32 / 800, navy | One per screen, the page title |
| Heading (h2) | `{typography.heading}` | 24 / 700 | Section headings, dialog titles |
| Subheading (h3) | `{typography.subheading}` | 20 / 700 | Sub-sections, the wordmark |
| Body | `{typography.body}` | 16 / 400 | All running text and inputs (16px stops iOS zoom on focus) |
| Small (label) | `{typography.label}` | 14 / 700 | Field labels, badges, nav on phone |
| Caption | `{typography.caption}` | 13 / 400 | Dates, masked card and account numbers, helper text |
| Amount | `{typography.amount}` | 16 / 700, tabular | Amounts in rows |
| Amount hero | `{typography.amount-hero}` | 32 / 800, tabular, navy | The disputed amount on review and detail |
| Mono | `{typography.mono}` | 14 | Error reference (`traceId`), dispute reference |

Amounts always use tabular numerals and right alignment in lists so columns of rands line up. Running text stops at 68ch so no line passes 80 characters. No all-caps labels, no eyebrow labels above headings, no middle-dot meta strings (row meta reads "Unrecognised, raised 26 Oct 2026"), and no arrows appended to or prefixed on link and button text ("Back to disputes", not "← Disputes"). The middle dot survives only inside the outcome badges ("Resolved · Upheld"), which are copy-deck text, and in `document.title`.

## Layout & Spacing

A 4-based spacing scale, the Tailwind subset 1, 2, 3, 4, 6, 8, 12, 16 (4 to 64px). Nothing outside it.

- **One header everywhere.** Signed in: wordmark, the pill nav (Transactions, Disputes) and Sign out. Linking: wordmark and Sign out (nothing else is reachable while unlinked). Signed out: wordmark, plus a secondary "Sign in" pill on the start page.
- **One footer everywhere.** A hairline, then two caption lines: "Demo data only. Every name, number and amount shown here is made up." and "Your details are used only to show your transactions and handle your disputes." No copyright line. The page is a column flex box, so the footer sits at the foot of short screens instead of leaving a void below it. The identity provider's hosted pages carry no DisputeDesk footer (the label policy can't add one).
- **Phone (from 360px):** single column, `{spacing.4}` (16px) side gutters, a floating pill tab bar (Transactions, Disputes) 16px above the foot, primary action full-width at the end of the content.
- **Tablet and desktop (from 768px):** content centred, `{spacing.6}` gutters, `{spacing.8}` between sections, `{spacing.16}` (64px) at the page foot as section spacing. Button groups are left-aligned with the content above them, primary first.
- **Content width:** lists max `{spacing.content-max}` (880px); forms and the linking flow max `{spacing.form-max}` (560px); the start page alone uses `{spacing.page-max}` (1120px) for its two-column hero.
- **Rhythm:** `{spacing.6}` between sections on phones; rows touch, divided by hairlines; `{spacing.6}` padding inside cards; `{spacing.3}` by `{spacing.4}` inside rows.
- **Touch targets:** every interactive element is at least `{spacing.tap-target}` (44px) tall; buttons and tab segments are 48px.

## Elevation & Depth

One thing floats per screen; everything else is flat. The dispute summary card carries the soft navy-tinted shadow (`0 4px 16px rgba(20,40,122,0.08)`); the start page's sample status card, the floating tab bar and dialogs carry the stronger overlay shadow, and the dialog scrim is navy at 35%. Lists, forms, the review read-back and empty states are flat white surfaces with a hairline `{colors.border}` edge, so the summary card is the only elevated object on a detail screen. No gradients. In dark mode shadows drop away and the slate surface step carries the depth.

## Shapes

Friendly and round, with radius that follows hierarchy instead of one radius on everything. **Pills (`{rounded.full}`) for every button**, status badges, the segmented nav and tab bar, the Copy button, step numbers, timeline dots and skeleton bars. `{rounded.lg}` (24px) for the summary card, form surfaces, empty states, dialogs and the hosted-login card. `{rounded.md}` (12px) for list panels, text fields, the reason group, OTP cells, notices and quote blocks: text fields stay rectangular-ish for legibility. **Rows have no radius**: they sit edge to edge on their panel. shadcn's `--radius` is `{rounded.md}`; the Button variant overrides to full.

## Components

shadcn components used as-is with the tokens above: `Button`, `Card`, `Input`, `Textarea`, `Label`, `RadioGroup`, `AlertDialog`, `Alert`, `Badge`, `Skeleton`, `Separator`, `Sonner` toast. Brand-layer and product components:

- **Button.** Pill, 48px tall, full-width on phone. Primary: `{colors.primary}` fill, white label, darker on hover and press. Secondary: white with a 1.5px cobalt outline and cobalt label, tint fill on hover. Destructive: `{colors.destructive}` fill, used only inside the withdraw confirmation. At most one primary button per screen.
- **Segmented nav.** Desktop: the two destinations as segments in a `{colors.muted}` pill track; the current one is a filled cobalt pill with a white label. Phone: the same segments in a white floating pill bar at the foot, icon beside label.
- **Start page.** Header with the wordmark and a secondary "Sign in" pill. A two-column hero from 1024px (stacked below): left, the display headline (two lines at 1280px), a one-sentence lede and the actions, left-aligned, "Sign in" primary and "Create a login" secondary; right, the page's one memorable element, a **sample status card** on a `{colors.primary-subtle}` stage. The card is the real dispute summary in miniature: merchant, reason and card ending, the amount in `{typography.amount-hero}`, a status badge, and a three-step timeline (Submitted, Under review as the current step, Resolved as an upcoming step with no date and "The outcome is added here when the bank decides"). Times are SAST. It is `aria-hidden` and captioned in text ("A sample dispute. Every dispute you raise gets a timeline like this one."). Below, "How it works" on a white band: three numbered steps (Link your login, Pick a card transaction, Follow its status). The numbers are navy on `{colors.muted}`, not cobalt, because they are not actions. Then the footer. The earlier full-width cobalt band is gone.
- **Status badge.** Pill, `{typography.label}`, status tone background and text from the table above, leading icon. Text is the customer-facing status exactly ("Under review (prioritised)", "Resolved · Upheld").
- **List panel and rows.** Transactions and disputes are rows on one white panel (`{rounded.md}`, hairline edge), not a stack of separate cards. Rows have no radius, 64px minimum height, and a hairline between them; day headings ("Mon 26 Oct", 14/700, muted, sentence case) sit inside the panel above their rows. Disputes uses one panel each for Open and Closed, with an h2 above each.
- **Transaction row.** Left: merchant (body, 700) over the masked card or account (caption, muted) and, for not-disputable rows, the hint with an info icon. Phone: the amount top right; a "Dispute" link under it; a status badge on its own right-aligned line under the row text, so long badges never squeeze the merchant name. From 768px: three columns, text, amount (`{spacing.amount-col}`, right-aligned, tabular) and action (`{spacing.action-col}`), so every amount lines up down the list. Credits show a leading "+" and no Dispute control.
- **Form surface.** The raise form is one flat surface: the transaction being disputed as a row at the top (merchant, date and card ending, amount right), a hairline, then the reasons, fields and actions. Linking forms and the review read-back use the same surface.
- **Reason group.** A `RadioGroup` drawn as one bordered group (`{colors.input}` edge, `{rounded.md}`) of six rows with hairlines between them: reason name (body, 700) and one-line explanation (caption). Selected: `{colors.primary-subtle}` fill and a 2px inset `{colors.primary}` ring.
- **Amount summary.** On review and detail: the disputed amount in `{typography.amount-hero}`, with the transaction amount and, for wrong amount, the expected amount below.
- **Dispute summary card.** The one elevated card on a detail screen: merchant as the h1, status badge, "Disputed amount" with the amount in `{typography.amount-hero}`, then a hairline and the reason, date, card and reference, then Withdraw where allowed.
- **Timeline.** A vertical 2px rail in `{colors.border}` joining one dot per entry. Earlier entries: a 12px outlined dot and the line in 600 weight. The current (latest) entry: a 16px cobalt dot with a 4px `{colors.primary-subtle}` halo and the line in navy 800. Under each line: the actor ("You" or "The bank", 700, foreground), then the SAST time (caption, muted), with no separator character; then any customer-visible note in a muted quote block. The line text is the copy-deck sentence ("The bank prioritised the review of your dispute."), not the badge text.
- **Empty state.** A flat surface (`{rounded.lg}`): an h2 saying what isn't here, one sentence on why or what fills it, and the next action where there is one ("Go to transactions").
- **Problem notice.** `Alert` in danger tone: plain sentence, what to do next, and "Reference" with the `traceId` in mono plus a pill Copy button.
- **OTP input.** Six cells, 48px wide on phone, 24px/800 digits, `{rounded.md}`, one real input underneath (`inputmode="numeric"`, `autocomplete="one-time-code"`).
- **Wordmark.** "DisputeDesk" in navy 20/800 with a small cobalt dot. It is a placeholder logo; the theme file and the label policy hold its asset path.

## Theme file and hosted-login theming

One file owns the theme: `frontend/src/theme/tokens.css`. It declares the `@font-face` rules and every token above as a CSS custom property under `:root`, the dark set under `@media (prefers-color-scheme: dark)`, and Tailwind v4's `@theme inline` maps them to utilities (`bg-primary`, `text-muted-foreground`, `rounded-md`). Components never use a raw hex value. The font files and their licence sit beside it in `fonts/` (`nunito-sans-latin-{400,600,700,800}-normal.woff2`, `OFL.txt`). [mockups/tokens.css](mockups/tokens.css) is the same token set, used by the mockups.

The identity provider's hosted sign-in and sign-up pages are themed from the same values through its label policy, generated at provisioning time (never hand-entered):

| Label policy field | Token | Light / dark value |
|---|---|---|
| Primary colour | `{colors.primary}` / `{colors.primary-dark}` | #2B57C9 / #8CAAF7 |
| Background colour | `{colors.background}` / `{colors.background-dark}` | #F4F5F7 / #12171E |
| Warning colour | `{colors.destructive}` / `{colors.destructive-dark}` | #B42318 / #FF8A7E |
| Font colour | `{colors.foreground}` / `{colors.foreground-dark}` | #1E2430 / #EEF1F5 |
| Logo and icon (light, dark) | the wordmark assets named in the theme file | |
| Font | Nunito Sans: upload the 400 WOFF2 from `fonts/` (the policy takes one font file) | |
| Theme mode | auto (follows the device) | |

The label policy has no radius or spacing fields, so the hosted pages keep their own button and field shapes; colour, font, logo and copy carry the brand across [ASSUMPTION: this partial match is acceptable]. See [mockups/sign-in.html](mockups/sign-in.html).

## Do's and Don'ts

| Do | Don't |
|---|---|
| Use `{colors.primary}` for actions: buttons, links, focus, selection, the active nav segment | Use the accent for status, icons, illustrations or decoration |
| Use navy `{colors.heading}` for large headlines only | Introduce a second accent colour, or red outside errors and destructive actions |
| Make every button a pill | Mix square and pill buttons |
| Pair every status tone with its text label and icon | Rely on colour alone to say where a dispute stands |
| Put every colour, size and radius in the theme file | Write a raw hex or pixel value in a component |
| Keep amounts tabular and right-aligned | Mix proportional numerals into amount columns |
| One primary button per screen | Two competing filled buttons |
| Keep the brand swappable and generic | Copy any real bank's colours, logos, imagery or typefaces |
| Elevate one card per screen; put lists on one panel as rows | Chop content into identical shadowed cards with one radius |
| Name the actor and time plainly ("The bank  22 Oct 2026, 20:46 SAST") | Join meta with middle dots, add eyebrow labels or all-caps labels |
| Keep display type for the start page headline | Use display sizes on app screens (their h1 is 32px) |
