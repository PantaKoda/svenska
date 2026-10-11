# Svenska – task-based Swedish learning app

## Goal (plain words)
A desktop app where I learn Swedish by **doing real tasks** (buy a ticket, pay at the
supermarket, book a doctor's time) through **listening and writing** exercises.
Each unit teaches only the words and grammar that task needs. Theory is never a lesson;
it is a **reference** I look up when I need it. I practise speaking on my own.

New themes (hospital, household, cleaning, construction, computers…) must be cheap to
add: write the vocabulary as data, draw one scene, and the standard exercises work on it.

Levels: A1, A2, B1, B2, C1/C2 (CEFR). Build A1 and A2 first.

---

## Core principles (do not break these)

1. **Task first.** Every unit starts from a concrete real-life task and ends with a
   simulated version of that task. No unit exists only to teach grammar.
2. **Two layers of content.**
   - **Core layer (generated):** word and pattern drills produced from theme packs +
     sentence frames by the drill generator. Same exercise styles for every theme.
   - **Authored layer (hand-written):** model dialogues, final tasks, listening texts,
     free writing. Unique per unit. Never generated from templates.
   A unit uses both. The core layer is the backbone, not the whole course.
3. **Controlled language.** A writing exercise may only use words and grammar introduced
   in the current unit or earlier. Listening/reading texts may contain a few unknown
   words (< 5 % of tokens), and every one must have a gloss.
4. **Theory is reference, not lessons.** Grammar explanations are short cards (one
   screen each) in a reference panel. Exercises link to the relevant card. Never write
   multi-page theory or units that drill one topic for days. Example: telling the time
   is a few exercises inside the *Buss & tåg* unit + one reference card, not a unit.
5. **Level guard.** Grammar must not appear before its level. The allowed grammar per
   level is listed in `content/levels.yaml`. That file follows the order of the Rivstart
   books (Natur & Kultur) and is maintained by me. Rivstart is used **only** as a level
   guard: never copy its texts, dialogues, exercises, images or audio.
6. **Content is data.** All course content lives in plain files under `content/`.
   The app only loads and plays it. No Swedish learning content hard-coded in C#.
7. **Correct Swedish.** Natural, standard Swedish (rikssvenska) as used in Sweden today.
   Göteborg settings are welcome (Västtrafik, spårvagn, Hisingen). If you are not sure a
   sentence or frame is natural, still write it but set `review: true` so I check it.
8. **Small steps.** Work milestone by milestone (see bottom). Each milestone must build,
   pass the validator and tests, and be runnable before the next starts.

---

## Tech stack

- **App:** C# / .NET 8+, Avalonia UI, MVVM (CommunityToolkit.Mvvm). Desktop now
  (Windows and Linux). Mobile may come later with Avalonia, so:
  - `Svenska.Core` contains **no UI and no platform-specific code**.
  - Audio playback goes behind an interface (`IAudioPlayer`) with a desktop
    implementation. Same for any other platform-specific service.
- **SVG:** Svg.Skia / Avalonia.Svg.Skia.
- **Audio playback:** pick a library that plays `.ogg` or `.mp3` on Windows and Linux
  (LibVLCSharp is the fallback). Do a small spike first and report the choice.
- **Storage:**
  - `data/saldo.db` – read-only SQLite word database built from SALDO (see tools).
  - `content/` – YAML course content (the source of truth).
  - user progress – SQLite file in the user's app-data folder (`progress.db`).
- **Tools:** Python 3 scripts in `tools/` for data import and audio generation.
  The **validator and drill generator are C#**, in the same solution, so they use the
  same content models as the app.

## Repository layout

```
/AGENTS.md
/src
  Svenska.Core/           content models, YAML loading, answer checking, scheduler,
                          frame filling (no UI, no platform code)
  Svenska.App/            Avalonia app (views, view models)
  Svenska.Validator/      console: validates all content, exits non-zero on errors
  Svenska.Generator/      console: generates core-layer drills from packs + frames
  Svenska.Tests/          xUnit tests
/content
  levels.yaml             grammar allowed per level (level guard)
  grammar.yaml            grammar points: id, level, prerequisites, reference card
  tags.yaml               the fixed list of semantic tags
  frames.yaml             generic sentence frames, usable with any theme
  themes/
    basic.yaml            pronouns, common verbs, function words, numbers
    matbutiken.yaml       one theme pack per theme
    stadning.yaml
  units/a1/04-matbutiken.yaml   one file per unit (authored layer + word selection)
  generated/a1-04.yaml    generator output (committed, do not edit by hand)
  svg/                    scenes drawn in Inkscape
  audio/                  generated audio, named by hash (do not edit by hand)
/data
  saldo.db                built by tools/saldo_to_sqlite.py (gitignored)
/tools
  saldo_to_sqlite.py      builds data/saldo.db from SALDO (already written)
  gen_audio.py            generates audio for every Swedish line in content/
```

---

## Content model

### levels.yaml
```yaml
A1:
  grammar: [pron_subject, present_tense, en_ett, def_singular, plural_indef,
            v2_word_order, yes_no_question, wh_question, negation_inte,
            numbers_0_1000, time_clock, modal_vill_kan_ska, possessive_basic,
            adjective_agreement_basic]
A2:
  grammar: [past_tense, perfect_tense, future_ska_kommer_att, plural_def,
            adjective_agreement_full, comparison, inversion_after_adverb,
            subordinate_clause_basic, imperative, reflexive_basic,
            preposition_time]
# B1+ added later. Themis checks this list against the Rivstart contents.
```

### grammar.yaml
```yaml
- id: def_singular
  level: A1
  requires: [en_ett]
  title: "Definite form, singular (bilen, huset)"
  card: |
    en-word → add -en / -n: en bil → bilen, en kaka → kakan
    ett-word → add -et / -t: ett hus → huset, ett äpple → äpplet
```
A card is at most ~15 lines: the rule, a table or pattern, 3–4 examples. No essays.
Cards are shown in a fixed-width font in the reference panel, so tables can be lined up
with spaces. Keep lines to 66 characters (the validator warns above that).

### tags.yaml – semantic tags
A small, fixed list (about 20–30). Tags say what a word *is* so frames only combine
words that make sense together. Add a new tag only when a theme really needs it,
and ask first.
```yaml
nouns:  [person, place, room, furniture, tool, device, surface, food, drink,
         container, clothing, body_part, vehicle, document, problem, time_unit]
verbs:  [action_surface,   # dammsuga, torka, moppa (+ surface)
         action_object,    # köpa, ta, lämna (+ concrete object)
         action_intrans,   # sova, jobba, vänta
         consume]          # äta, dricka (+ food/drink)
adjectives: [state]        # trasig, ren, smutsig, tom, full
```

### Theme packs (`content/themes/*.yaml`)
The vocabulary of a theme, across all levels. This replaces a central lexicon:
the lexicon is the union of all packs.
```yaml
theme: stadning
title: "Städning"
scene: svg/stadning.svg
words:
  - { lemma: dammsugare, pos: noun, gender: en, tags: [tool, device], level: A1,
      gloss: vacuum cleaner, svg: w-dammsugare }
  - { lemma: hink,  pos: noun, gender: en,  tags: [tool, container], level: A1, gloss: bucket }
  - { lemma: golv,  pos: noun, gender: ett, tags: [surface], level: A1, gloss: floor }
  - { lemma: dammsuga, pos: verb, tags: [action_surface], level: A1, gloss: to vacuum }
  - { lemma: trasig, pos: adj, tags: [state], level: A1, gloss: broken }
  - { lemma: rengöringsmedel, pos: noun, gender: ett, tags: [substance], level: A2,
      gloss: detergent }
frames:            # theme-specific frames (3–6 per theme), same syntax as frames.yaml
  - { id: stad-01, level: A1, grammar: [modal_vill_kan_ska],
      sv: "Jag ska {action_surface.inf} {surface.def}.",
      en: "I am going to {action_surface} the {surface}." }
```
- `level` is the earliest level the word may be taught. A word is **known** only once
  a unit lists it in `new_words`.
- Inflected forms come from `saldo.db`. If a word is missing there, add `forms:` by hand.
- `svg` is the element id in the theme scene (ids are `w-<lemma>`, ASCII only:
  å→a, ä→a, ö→o).

### Frames (`content/frames.yaml` and in packs)
A frame is a sentence with slots. Generic frames work for every theme:
```yaml
- { id: gen-001, level: A1, grammar: [wh_question, def_singular],
    sv: "Var är {tool|container|device.def}?", en: "Where is the {1}?" }
- { id: gen-002, level: A1, grammar: [yes_no_question],
    sv: "Har du {tool|food|drink.indef}?", en: "Do you have {1.a}?" }
- { id: gen-003, level: A1, grammar: [adjective_agreement_basic, def_singular],
    sv: "{tool|device|furniture.def} är {state~1}.", en: "The {1} is {2}." }
- { id: gen-010, level: A2, grammar: [perfect_tense],
    sv: "Jag har redan {action_surface.sup} {surface.def}.",
    en: "I have already {1.pp} the {2}." }
```
Slot syntax: `{tags.form}`
- `tags`: one or more tags joined with `|` (any of them fits).
- `form`: `indef`, `def`, `pl`, `pl_def`, `a` (nouns; `a` = with article: *en påse*,
  *ett kvitto*); `inf`, `pres`, `past`, `sup`,
  `imp` (verbs); `~N` for an adjective that agrees with slot N (`trasig` / `trasigt` /
  `trasiga`).
- In `en`, `{N}` is the gloss of slot N; `.a` adds a/an; `.pp` and `.past` use the past
  participle and the past tense. Irregular English forms are stored in the gloss after
  a semicolon: `gloss: "to buy; past: bought; pp: bought"`, `gloss: "loaf; pl: loaves"`.
- The fixed words of a generic frame must be in the `basic` pack; a frame is only used
  for a unit that knows all its fixed words and all its grammar.
Each frame has `review: true` until I have checked it. One bad frame produces bad
sentences in every theme, so frames are checked once, carefully.

### Unit file (`content/units/a1/04-matbutiken.yaml`)
```yaml
id: a1-04
level: A1
theme: matbutiken            # main theme pack; may also use words from others
title: "I matbutiken"
task: "Find groceries, understand the price and pay at the checkout."
can_do: "I can buy food and pay in a Swedish supermarket."
new_words: [mjölk, bröd, ost, kassa, kvitto, påse, kosta, krona, ...]   # ≤ 25
new_grammar: [def_singular]                                             # ≤ 2
scene: svg/matbutik.svg

core:                        # what the generator should produce for this unit
  templates: [scene_click, inflect, cloze, dictation, word_order, translate]
  items_per_template: 6

model_dialogue:              # authored
  - { speaker: kassör, sv: "Hej! Behöver du en påse?", en: "Hi! Do you need a bag?" }
  - { speaker: du,     sv: "Nej tack.",                en: "No thanks." }

exercises: [...]             # authored extra exercises (numbers, listen_choose...)
final_task:                  # authored
  type: dialogue
  turns: [...]
speaking_prompt: "Say aloud: ask where the milk is, then ask what the cheese costs."
```
Temporary: a unit may have `assumes: { words: [..], grammar: [..] }` for what it takes as
known from earlier units that are not written yet. The validator warns while it exists;
remove it when units 01–03 are written (M6).

### Exercise types
| type | skill | layer | what happens |
|---|---|---|---|
| `dictation` | listen + write | core / authored | play audio, user types the sentence |
| `cloze` | write | core / authored | sentence with a gap, user types the missing word |
| `inflect` | write | core | given lemma + target form ("bil → definite plural") |
| `word_order` | write | core / authored | user arranges tokens into a sentence |
| `translate` | write | core / authored | English → Swedish, several accepted answers |
| `scene_click` | listen | core | hear a word, click it in the SVG scene |
| `listen_choose` | listen | core / authored | play audio, choose meaning/picture/answer |
| `numbers` | listen | authored | hear a price/time/phone number, type the digits |
| `dialogue` | listen + write | authored | scripted task: each turn choose or type a reply |
| `free_write` | write | authored | short free text + checklist (no auto-grading yet) |

Fields per type (the four written types, settled in M1):
- `dictation`: `sv` (played and expected), `en` (shown after checking).
- `cloze`: `sv` with one gap `___`, `answer`, optional `hint` (the base form), `en`.
- `inflect`: `lemma`, `form` (same names as in frames: `def`, `pl`, `pres` …), `answer`.
  The validator checks the answer against SALDO.
- `word_order`: `sv` (the tokens are shuffled by the app), `en`.
- `translate`: `en` (the prompt), `sv` (the answer), `accept` for other good answers.
  The generator fills `accept` with its other sentences that have the same English.

Every item that checks an answer has `answer` and optional `accept: [..]`.
Every item has `grammar: [..]` tags and optional `review: true`.

---

## Drill generator (Svenska.Generator)

Plain words: takes the words a unit teaches, fills them into sentence frames, and writes
ready-made exercises to `content/generated/<unit>.yaml`.

For unit U:
1. **Known words** = all `new_words` of U and earlier units. **Focus words** = U's
   `new_words`.
2. **Usable frames** = frames whose level ≤ U's level and whose grammar tags are all
   introduced in U or earlier.
3. Fill slots with known words whose tags match. Every generated sentence must contain
   **at least one focus word**. Prefer combinations that also reuse older words.
4. Inflect with `saldo.db`. If a form is missing or ambiguous, skip and report it.
5. Turn sentences into items for each template listed in the unit's `core`
   (cloze: blank the focus word; word_order: shuffle tokens; translate: use `en`;
   dictation: audio of `sv`; inflect and scene_click work on single words).
6. **Deterministic:** fixed random seed per unit, so regenerating gives the same output
   unless the content changed.
7. Each item records its source: `frame: gen-003, words: [hink, trasig]`.
   Items from a frame with `review: true` inherit `review: true`.

Run it with `dotnet run --project src/Svenska.Generator`. To see what a theme pack gives
without a unit, add `--theme stadning` (prints the drills, writes nothing).

Generated files are committed and validated like any other content. The app draws
randomly from this checked pool at runtime; it never generates sentences itself.

---

## Answer checking (Svenska.Core)

- Normalise: trim, collapse spaces, ignore case at sentence start and final punctuation.
- Never ignore å/ä/ö differences, but if the only difference is a missing diacritic
  (`a` for `ä`), say so specifically.
- Word-level diff for wrong answers: show which words were wrong.
- **Near-miss detection with SALDO:** if the user's word is a form of the right lemma
  but the wrong form, say "Right word, wrong form: expected *mjölken* (definite)".
- Feedback is one or two short lines plus a link to the grammar card.

## Validator (Svenska.Validator) – must pass before any commit
Fails if:
1. A YAML file does not match the model.
2. A writing exercise (authored or generated) contains a lemma that is not known by
   that unit. (Tokenise, look up each token's lemma in `saldo.db`; if a form maps to
   several lemmas, it passes if any of them is known. Numbers and names listed in the
   unit's `names:` are exempt.)
3. A listening text has > 5 % unknown tokens, or an unknown token has no gloss.
4. A grammar tag is used before its level in `levels.yaml`, or before the unit that
   introduces it. This applies to frames too.
5. A unit has > 25 new words or > 2 new grammar points, or lists a word whose pack
   `level` is above the unit's level.
6. A word is in two theme packs, or a unit's `new_words` lists a word that is in no pack.
7. A frame uses an unknown tag or form, or a slot that no word in any pack can fill.
8. A generated file is out of date with its inputs (run the generator).
9. A new word is never used in any exercise of its unit.
10. A word introduced at least 3 units ago has never been reused since (warning).
11. An audio file referenced is missing (warning – run gen_audio.py).
Output: a short report per unit, plus totals (words per level, frames and items with
`review: true`).

## Audio (tools/gen_audio.py)
- Offline TTS with Piper using a Swedish voice (check the current Piper voice list).
- Collect every Swedish line that is played (authored and generated), hash
  (text + voice + speed), write `content/audio/<hash>.ogg`. Skip existing files.
- Produce normal speed and a slower version for dictation.
- Optional later: Azure neural voices for better quality, behind a flag.

## SALDO database (tools/saldo_to_sqlite.py)
Already written. Source: SALDOs morfologi, Språkbanken, CC-BY 4.0.
The app must show "Data: SALDO, Språkbanken (CC-BY 4.0)" on the About screen.
Known bug to fix first: the multi-word check uses `pos.endswith("m")`, which also
matches the proper-name code `pm`. Treat `pm` as a single-word proper name and only
`pmm` (and other `…m` codes) as multi-word. Add a test for it.

---

## App screens

1. **Home:** level → list of units with progress; "Review" button with due count.
2. **Unit:** task + can-do at top, then steps: Words (scene + list) → Listen (model
   dialogue, transcript hidden until requested) → Exercises (core + authored mixed)
   → Final task.
3. **Exercise player:** one item at a time, keyboard-first (Enter = check / next),
   replay audio (Ctrl+Space), slow audio (Ctrl+Shift+Space).
4. **Reference panel** (opens beside anything, never blocks the exercise):
   - Word lookup: type any form → lemma, gloss (if in a pack), en/ett, all forms.
   - Grammar cards, filtered to my current level by default.
5. **Review:** spaced repetition of words and missed items. Start with a simple
   Leitner box scheduler; keep the interface so FSRS can replace it later.
6. **Theme practice:** pick any theme pack and practise its known words with the core
   templates, outside the unit order.

Swedish characters must be easy to type on any keyboard: buttons for å ä ö and
shortcuts (e.g. Alt+a / Alt+e / Alt+o or similar).

---

## A1 unit plan (draft – order may change)

| # | Theme | Task |
|---|---|---|
| 01 | Jag | Greet, say your name, where you come from, spell your name |
| 02 | Jag | Give phone number, address and personnummer on a form |
| 03 | Vardag | Say what you do on a normal day; days of the week |
| 04 | Matbutiken | Find groceries, understand prices, pay |
| 05 | Buss & tåg | Buy a ticket, read departure times (clock time here) |
| 06 | Bostad | Name rooms and things at home, say what is where |
| 07 | Familjen | Talk about your family |
| 08 | Hälsa | Say what hurts, understand simple instructions |
| 09 | Fritid & väder | Make a simple plan with a friend, talk about the weather |
| 10 | Arbete | Say what you work with and when |

A2 revisits the same themes with harder tasks (e.g. Bostad: book the tvättstuga,
report a fault to the landlord; Buss & tåg: understand announcements and delays).
Work themes (städning, bygg, vård, data) are added as theme packs and used both in
units and in Theme practice.

---

## Milestones

- **M0 – Skeleton.** Solution and projects, content models, YAML loading,
  validator running on an empty course, saldo.db built and the `pm` bug fixed,
  audio playback spike behind `IAudioPlayer`. Tests for loading and SALDO lookup.
- **M1 – One unit end to end (authored).** Write unit a1-04 *I matbutiken*
  (≈ 20 words, 1–2 grammar points, ~15 authored items), the `matbutiken` and `basic`
  packs it needs. Implement `dictation`, `cloze`, `inflect`, `word_order` with answer
  checking. Generate audio. Must pass the validator.
- **M2 – Theme packs and drill generator.** `tags.yaml`, ~10 generic frames,
  Svenska.Generator, validator rules 6–8. Generate the core drills for a1-04. Then add
  a second pack (`stadning`) and show that its drills come out with no code changes.
  Report every frame and generated item that looks unnatural.
- **M3 – Reference panel.** Word lookup via saldo.db, grammar cards.
- **M4 – Progress and review.** progress.db, unit progress, Leitner review queue,
  Theme practice screen.
- **M5 – Rich exercises.** `scene_click` (spike how to hit-test SVG elements by id with
  Svg.Skia first), `numbers`, `listen_choose`, `dialogue` final task.
- **M6 – Fill A1.** Units 01–10. Then A2.

## Rules for you (the agent)
- Read this file before each task. Stay inside the current milestone.
- Ask before changing the content schema, adding a tag, or adding a dependency.
- Run the generator, validator and tests before saying a milestone is done; report
  results and the number of frames and items marked `review: true`.
- Prefer small, plain code over frameworks. No telemetry, no network calls at runtime.
  The one exception is the **Update** button: only when clicked, it asks GitHub for the
  latest release and installs it (`Svenska.Core/Updates`).
- Releases: push a tag `vX.Y.Z`; `.github/workflows/release.yml` publishes the zips the
  Update button installs. Never commit secrets, `data/`, or the Rivstart PDFs.
- When writing content, write fewer, better items rather than many repetitive ones,
  and recycle words from earlier units on purpose.
- Never hand-edit files in `content/generated/` or `content/audio/`.
