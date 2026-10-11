"""Run with:  python -m unittest discover tools"""
import contextlib
import io
import os
import sqlite3
import tempfile
import unittest

import saldo_to_sqlite


def entry(word, pos, inherent, forms):
    word_forms = "".join(
        f'<WordForm><feat att="writtenForm" val="{form}"/><feat att="msd" val="{msd}"/></WordForm>'
        for form, msd in forms)
    return (
        "<LexicalEntry><Lemma><FormRepresentation>"
        f'<feat att="writtenForm" val="{word}"/><feat att="partOfSpeech" val="{pos}"/>'
        f'<feat att="inherent" val="{inherent}"/><feat att="paradigm" val="{pos}_test"/>'
        f"</FormRepresentation></Lemma>{word_forms}</LexicalEntry>")


SAMPLE = '<?xml version="1.0" encoding="UTF-8"?><LexicalResource><Lexicon>' + "".join([
    entry("bil", "nn", "u", [("bil", "sg indef nom"), ("bilen", "sg def nom")]),
    entry("hus", "nn", "n", [("hus", "sg indef nom"), ("huset", "sg def nom")]),
    entry("Göteborg", "pm", "n", [("Göteborg", "nom"), ("Göteborgs", "gen")]),
    entry("New York", "pmm", "n", [("New York", "nom")]),
    entry("test", "nn", "v", [("test", "sg indef nom")]),
    entry("byxor", "nn", "p", [("byxor", "pl indef nom")]),
    entry("kl", "nna", "u", [("kl", "sg indef nom")]),
    entry("ta upp", "vbm", "", [("ta upp", "inf aktiv"), ("tar upp", "pres ind aktiv")]),
]) + "</Lexicon></LexicalResource>"


class SaldoToSqliteTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        xml_path = os.path.join(cls.tmp.name, "sample.xml")
        with open(xml_path, "w", encoding="utf-8") as f:
            f.write(SAMPLE)
        cls.db_path = os.path.join(cls.tmp.name, "saldo.db")
        with contextlib.redirect_stdout(io.StringIO()):
            saldo_to_sqlite.main(xml_path, cls.db_path)
        cls.db = sqlite3.connect(cls.db_path)

    @classmethod
    def tearDownClass(cls):
        cls.db.close()
        cls.tmp.cleanup()

    def lemma(self, word):
        return self.db.execute(
            "SELECT pos, pos_name, gender, multiword FROM lemma WHERE word = ?", (word,)).fetchone()

    def test_single_word_proper_name_is_not_multiword(self):
        self.assertEqual(self.lemma("Göteborg"), ("pm", "proper name", None, 0))

    def test_multiword_proper_name(self):
        self.assertEqual(self.lemma("New York"), ("pmm", "proper name", None, 1))

    def test_particle_verb_is_multiword(self):
        self.assertEqual(self.lemma("ta upp"), ("vbm", "verb", None, 1))

    def test_noun_gender(self):
        self.assertEqual(self.lemma("bil"), ("nn", "noun", "en", 0))
        self.assertEqual(self.lemma("hus"), ("nn", "noun", "ett", 0))

    def test_noun_with_both_genders_and_plural_only_noun(self):
        self.assertEqual(self.lemma("test"), ("nn", "noun", "en/ett", 0))
        self.assertEqual(self.lemma("byxor"), ("nn", "noun", None, 0))

    def test_abbreviation_keeps_the_name_and_gender_of_its_word_class(self):
        self.assertEqual(self.lemma("kl"), ("nna", "noun", "en", 0))

    def test_forms_are_linked_to_their_lemma(self):
        rows = self.db.execute(
            "SELECT l.word, f.msd FROM form f JOIN lemma l ON l.id = f.lemma_id WHERE f.form = 'bilen'"
        ).fetchall()
        self.assertEqual(rows, [("bil", "sg def nom")])

    def test_is_multiword(self):
        for pos in ("pm", "nn", "vb", ""):
            self.assertFalse(saldo_to_sqlite.is_multiword(pos), pos)
        for pos in ("pmm", "nnm", "vbm", "abm"):
            self.assertTrue(saldo_to_sqlite.is_multiword(pos), pos)


if __name__ == "__main__":
    unittest.main()
