# Svenska

A desktop app for learning Swedish by doing real tasks (buy a ticket, pay at the
supermarket, book a doctor's time) through listening and writing exercises.
The design and the milestones are in [AGENTS.md](AGENTS.md).

Status: milestone M6, level A1 is written: ten units, from greeting someone to talking about your
job. Each unit has its words, a model dialogue with
audio, dictation, cloze, inflect, word-order, translation, listen-and-choose, numbers and
click-in-the-scene exercises, and a final task as a dialogue). The drill generator
fills the unit's words into sentence frames; its output is in `content/generated/`.
The reference panel (F1) looks up any word form in SALDO and shows the grammar cards.
Progress and the review queue (Leitner boxes) are kept in `progress.db` in your app-data
folder; Theme practice runs the standard exercises on any theme pack.

## Install

Download the zip for your system from the
[latest release](https://github.com/PantaKoda/svenska/releases/latest), unpack it anywhere
and run `Svenska.App`. The **Update** button in the app installs the latest release over
your copy and restarts. Your progress is stored in your user profile and is not touched.

## Build from source

Needs the .NET SDK 10 (the projects target .NET 8) and Python 3.

```bash
dotnet test Svenska.slnx
dotnet run --project src/Svenska.Validator
dotnet run --project src/Svenska.App
```

The word database is not in the repository. Build it once (about 250 MB download):

```bash
mkdir -p data/raw
curl -L -o data/raw/saldom.xml https://svn.spraakbanken.gu.se/sb-arkiv/pub/lmf/saldom/saldom.xml
python tools/saldo_to_sqlite.py data/raw/saldom.xml data/saldo.db
```

## Audio

The spoken lines are generated offline with [Piper](https://github.com/OHF-Voice/piper1-gpl)
and committed under `content/audio/`. After changing Swedish text, run
`tools/gen_audio.py` (setup is described at the top of that file).

## Release a version

Every push to `main` is built, tested and validated on Windows and Linux.
To publish a version, tag it:

```bash
git tag v0.2.0
git push origin v0.2.0
```

The release workflow builds the Windows and Linux zips and publishes them with their
SHA-256 checksums.

## Data

Word forms: SALDO, Språkbanken (CC-BY 4.0). Språkbanken (2017). SALDOs morfologi.
https://doi.org/10.23695/agcm-ny22
