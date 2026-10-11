"""
Build a local SQLite database of Swedish words, their part of speech,
noun gender (en/ett) and all inflected forms, from SALDO's morphology.

GOAL (in plain words): turn one big dictionary file into a small, fast
database your Swedish-learning app can query offline, e.g.
"give me all forms of 'bil'" or "is 'hus' an en-word or an ett-word?".

DATA SOURCE
  SALDOs morfologi (saldom.xml), Språkbanken, updated 2017-09-19,
  128,036 entries, licence CC-BY-4.0.
  Page:     https://sprakbanken.se/resurser/saldom
  Download: https://svn.spraakbanken.gu.se/sb-arkiv/pub/lmf/saldom/saldom.xml  (~242 MB)
  Cite:     Språkbanken (2017). SALDOs morfologi. https://doi.org/10.23695/agcm-ny22
  CC-BY requires attribution: show "Data: SALDO, Språkbanken (CC-BY 4.0)" in your app.

USAGE
  python saldo_to_sqlite.py saldom.xml saldo.db
"""
import sqlite3
import sys
import xml.etree.ElementTree as ET

# Human-readable labels for SALDO part-of-speech codes (common ones only).
POS_NAMES = {
    "nn": "noun", "vb": "verb", "av": "adjective", "ab": "adverb",
    "pp": "preposition", "pn": "pronoun", "kn": "conjunction",
    "sn": "subjunction", "in": "interjection", "nl": "numeral",
    "pm": "proper name",
}
# 'v' = SALDO lists the noun with both genders (ett/en test). 'p' = plural only, no gender.
GENDER_NAMES = {"u": "en", "n": "ett", "v": "en/ett"}


def is_multiword(pos):
    """SALDO marks multi-word entries with an 'm' suffix on the two-letter code,
    e.g. 'vbm' = particle verb ("ta upp"), 'pmm' = multi-word name ("New York").
    'pm' itself is a single-word proper name ("Göteborg"), not a multi-word entry."""
    return len(pos) == 3 and pos.endswith("m")


def base_pos(pos):
    """'vbm' -> 'vb', 'pmm' -> 'pm', and likewise for abbreviations ('nna') and
    hyphenated forms ('nnh'). Other codes are returned unchanged."""
    return pos[:2] if len(pos) == 3 and pos[2] in "mah" else pos


def feats(elem):
    """Collect <feat att=... val=...> children into a dict."""
    return {f.get("att"): f.get("val") for f in elem.findall("feat")}


def main(xml_path, db_path):
    db = sqlite3.connect(db_path)
    db.executescript("""
        DROP TABLE IF EXISTS lemma;
        DROP TABLE IF EXISTS form;
        CREATE TABLE lemma (
            id        INTEGER PRIMARY KEY,
            word      TEXT NOT NULL,    -- base form, e.g. 'bil'
            pos       TEXT,             -- SALDO code, e.g. 'nn'
            pos_name  TEXT,             -- 'noun'
            gender    TEXT,             -- 'en' / 'ett' / 'en/ett' for nouns, else NULL
            paradigm  TEXT,             -- inflection class, e.g. 'nn_2u_bil'
            multiword INTEGER           -- 1 for 'ta upp', 'känna sig' ...
        );
        CREATE TABLE form (
            lemma_id  INTEGER REFERENCES lemma(id),
            form      TEXT NOT NULL,    -- e.g. 'bilarna'
            msd       TEXT              -- grammar tag, e.g. 'pl def nom'
        );
    """)

    lemma_rows, form_rows = [], []
    next_id = 1

    # iterparse streams the file so we never hold all 242 MB in memory.
    for _, elem in ET.iterparse(xml_path, events=("end",)):
        if elem.tag != "LexicalEntry":
            continue

        lemma_el = elem.find("Lemma/FormRepresentation")
        lf = feats(lemma_el) if lemma_el is not None else {}
        if not lf.get("writtenForm"):
            elem.clear()
            continue
        pos = lf.get("partOfSpeech", "")

        lemma_rows.append((
            next_id,
            lf.get("writtenForm"),
            pos,
            POS_NAMES.get(base_pos(pos)),
            GENDER_NAMES.get(lf.get("inherent")) if base_pos(pos) == "nn" else None,
            lf.get("paradigm"),
            1 if is_multiword(pos) else 0,
        ))
        for wf in elem.findall("WordForm"):
            wfeats = feats(wf)
            if wfeats.get("writtenForm"):
                form_rows.append((next_id, wfeats.get("writtenForm"), wfeats.get("msd")))

        next_id += 1
        elem.clear()  # free memory for the processed entry

        if len(form_rows) > 200_000:  # write in batches
            db.executemany("INSERT INTO lemma VALUES (?,?,?,?,?,?,?)", lemma_rows)
            db.executemany("INSERT INTO form VALUES (?,?,?)", form_rows)
            lemma_rows.clear(); form_rows.clear()

    db.executemany("INSERT INTO lemma VALUES (?,?,?,?,?,?,?)", lemma_rows)
    db.executemany("INSERT INTO form VALUES (?,?,?)", form_rows)
    db.executescript("""
        CREATE INDEX idx_lemma_word ON lemma(word);
        CREATE INDEX idx_form_form  ON form(form);
        CREATE INDEX idx_form_lemma ON form(lemma_id);
    """)
    db.commit()
    db.close()
    print(f"Done: {next_id - 1} lemmas written to {db_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: python saldo_to_sqlite.py saldom.xml saldo.db")
    main(sys.argv[1], sys.argv[2])

# Example queries once built:
#   -- all forms of 'bil'
#   SELECT f.form, f.msd FROM form f JOIN lemma l ON l.id = f.lemma_id
#   WHERE l.word = 'bil' AND l.pos = 'nn';
#
#   -- which lemma does 'sprang' belong to? (reverse lookup, great for reading practice)
#   SELECT l.word, l.pos_name, f.msd FROM form f JOIN lemma l ON l.id = f.lemma_id
#   WHERE f.form = 'sprang';
