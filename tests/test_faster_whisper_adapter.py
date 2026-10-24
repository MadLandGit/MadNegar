import contextlib
import importlib.util
import io
import tempfile
import unittest
import wave
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("madnegar_adapter", ROOT / "FasterWhisperAdapter.py")
assert SPEC and SPEC.loader
adapter = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(adapter)


class FakeSegment:
    def __init__(self, text: str):
        self.text = text


class FakeModel:
    def transcribe(self, audio_path, language=None, **kwargs):
        if language == "fa":
            text = "سلام دنیا"
            detected = "fa"
        elif language == "en":
            text = "hello world from model"
            detected = "en"
        else:
            text = "hello world from model"
            detected = "en"
        return [FakeSegment(text)], SimpleNamespace(language=detected, language_probability=0.8)


class FinglishModel:
    def transcribe(self, audio_path, language=None, **kwargs):
        if language == "fa":
            text, detected = "سلام چطوری", "fa"
        else:
            text, detected = "salam chetori", "en"
        return [FakeSegment(text)], SimpleNamespace(language=detected, language_probability=0.62)


class MixedFinglishWithUploadModel:
    """Models common Persian phrases rendered in Latin script unless Persian is explicitly requested."""
    def __init__(self):
        self.languages_requested = []
        self.prompts = []

    def transcribe(self, audio_path, language=None, **kwargs):
        self.languages_requested.append(language)
        self.prompts.append(kwargs.get("initial_prompt"))
        if language == "fa":
            text, detected, probability = "امروز باید این فایل رو Upload کنم", "fa", 0.82
        elif language == "en":
            text, detected, probability = "today I need to upload this file", "en", 0.88
        else:
            text, detected, probability = "emrooz bayad in file ro upload konam", "en", 0.73
        return [FakeSegment(text)], SimpleNamespace(language=detected, language_probability=probability)


class PersianAutoModel:
    def transcribe(self, audio_path, language=None, **kwargs):
        if language == "en":
            text, detected = "please open the API settings", "en"
        else:
            text, detected = "لطفاً تنظیمات API را باز کن", "fa"
        return [FakeSegment(text)], SimpleNamespace(language=detected, language_probability=0.86)


class EnglishMisdirectedAsPersianModel:
    def transcribe(self, audio_path, language=None, **kwargs):
        if language == "en":
            text, detected, confidence = "please open the API settings", "en", 0.89
        else:
            text, detected, confidence = "please open the API settings", "fa", 0.81
        return [FakeSegment(text)], SimpleNamespace(language=detected, language_probability=confidence)


class PersianMisdirectedAsEnglishModel:
    def transcribe(self, audio_path, language=None, **kwargs):
        if language == "fa":
            text, detected, confidence = "لطفاً تنظیمات را باز کن", "fa", 0.88
        else:
            text, detected, confidence = "لطفاً تنظیمات را باز کن", "en", 0.82
        return [FakeSegment(text)], SimpleNamespace(language=detected, language_probability=confidence)


class MixedScriptModel:
    def transcribe(self, audio_path, language=None, **kwargs):
        text = "این API را در Visual Studio باز کن"
        detected = "fa" if language is None else language
        return [FakeSegment(text)], SimpleNamespace(language=detected, language_probability=0.84)

class LowConfidencePersianAsLatinModel:
    def __init__(self):
        self.languages_requested = []

    def transcribe(self, audio_path, language=None, **kwargs):
        self.languages_requested.append(language)
        if language == "fa":
            return [FakeSegment("این یک آزمایش فارسی است")], SimpleNamespace(language="fa", language_probability=0.78)
        return [FakeSegment("in yek azmayesh farsi ast")], SimpleNamespace(language="en", language_probability=0.31)


class LowConfidenceEnglishModel:
    def __init__(self):
        self.languages_requested = []

    def transcribe(self, audio_path, language=None, **kwargs):
        self.languages_requested.append(language)
        if language == "fa":
            # A forced Persian pass that does not independently support Persian should be rejected.
            return [FakeSegment("سلام")], SimpleNamespace(language="fa", language_probability=0.28)
        return [FakeSegment("technical documentation may be confusing")], SimpleNamespace(language="en", language_probability=0.38)


class MixedScriptMisdirectedModel:
    def __init__(self):
        self.languages_requested = []

    def transcribe(self, audio_path, language=None, **kwargs):
        self.languages_requested.append(language)
        # The global label is wrong, but both scripts are genuinely present.
        return [FakeSegment("این API را در Visual Studio باز کن")], SimpleNamespace(language="en" if language is None else language, language_probability=0.83)


class DualMixedScriptModel:
    def __init__(self, text: str, detected: str, probability: float):
        self.text = text
        self.detected = detected
        self.probability = probability
        self.calls = 0

    def transcribe(self, audio_path, language=None, **kwargs):
        self.calls += 1
        return [FakeSegment(self.text)], SimpleNamespace(language=self.detected, language_probability=self.probability)


def make_wav(path: Path, seconds: float, sample_rate: int = 16000):
    frame_count = int(seconds * sample_rate)
    with wave.open(str(path), "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(sample_rate)
        writer.writeframes(b"\x00\x00" * frame_count)


class AutoLanguageAdapterTests(unittest.TestCase):
    def test_merge_overlapping_words(self):
        self.assertEqual(adapter.merge_segment_texts(["سلام this is", "this is a test"]), "سلام this is a test")

    def test_short_wav_is_not_split(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "short.wav"
            make_wav(source, 4)
            self.assertEqual(adapter.split_wave_file(str(source), str(Path(temp) / "chunks")), [str(source)])

    def test_long_wav_is_split_with_audio_headers(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "long.wav"
            output = Path(temp) / "chunks"
            make_wav(source, 22)
            chunks = adapter.split_wave_file(str(source), str(output))
            self.assertGreaterEqual(len(chunks), 2)
            for chunk in chunks:
                with wave.open(chunk, "rb") as reader:
                    self.assertEqual(reader.getframerate(), 16000)
                    self.assertEqual(reader.getnchannels(), 1)
                    self.assertEqual(reader.getsampwidth(), 2)
                    self.assertGreater(reader.getnframes(), 0)

    def test_prefer_persian_does_not_force_persian_for_clear_english(self):
        self.assertEqual(adapter.transcribe_auto(FakeModel(), "unused.wav", "prefer-persian"), "hello world from model")

    def test_balanced_auto_preserves_clear_english_result(self):
        self.assertEqual(adapter.transcribe_auto(FakeModel(), "unused.wav", "balanced"), "hello world from model")

    def test_persian_first_preference_corrects_finglish_even_in_standard_balanced_auto(self):
        model = MixedFinglishWithUploadModel()
        expected = "امروز باید این فایل رو Upload کنم"
        result = adapter.transcribe_auto(model, "unused.wav", "balanced", "persian-first")
        self.assertEqual(result, expected)
        self.assertEqual(model.languages_requested, [None, "fa"])
        self.assertIsNotNone(model.prompts[0])

    def test_persian_first_retry_does_not_force_clear_english(self):
        model = FakeModel()
        self.assertEqual(adapter.transcribe_auto(model, "unused.wav", "balanced", "persian-first"), "hello world from model")

    def test_english_first_also_recovers_finglish_without_translating_english(self):
        model = MixedFinglishWithUploadModel()
        expected = "امروز باید این فایل رو Upload کنم"
        result = adapter.transcribe_auto(model, "unused.wav", "balanced", "english-first")
        self.assertEqual(result, expected)
        self.assertEqual(model.languages_requested, [None, "fa"])
        self.assertEqual(model.prompts[0], adapter.PERSIAN_FIRST_TRANSCRIPTION_PROMPT)
        self.assertEqual(adapter.ENGLISH_FIRST_TRANSCRIPTION_PROMPT, adapter.PERSIAN_FIRST_TRANSCRIPTION_PROMPT)

    def test_both_preferences_keep_clear_english_as_latin(self):
        self.assertEqual(adapter.transcribe_auto(FakeModel(), "unused.wav", "balanced", "english-first"), "hello world from model")
        self.assertEqual(adapter.transcribe_auto(FakeModel(), "unused.wav", "balanced", "persian-first"), "hello world from model")

    def test_upload_alias_is_restored_to_latin_in_a_persian_sentence(self):
        self.assertEqual(adapter.restore_english_technical_terms("امروز باید این فایل رو آپلود کنم"), "امروز باید این فایل رو Upload کنم")

    def test_google_drive_alias_is_restored_to_latin_in_a_persian_sentence(self):
        self.assertEqual(adapter.restore_english_technical_terms("بعد گوگل درایو رو باز کن"), "بعد Google Drive رو باز کن")

    def test_technical_term_guard_does_not_rewrite_prefix_inside_zwnj_compounds(self):
        for text in ("در گیت\u200cسازی", "پایتون\u200cکار", "این گیت\u200cسازی و پایتون\u200cکار است"):
            with self.subTest(text=text):
                self.assertEqual(adapter.restore_english_technical_terms(text), text)

    def test_zwnj_separated_technical_aliases_still_restore_as_whole_terms(self):
        self.assertEqual(
            adapter.restore_english_technical_terms("گیت\u200cهاب و ویژوال\u200cاستودیو"),
            "GitHub و Visual Studio",
        )

    def test_balanced_auto_recovers_recognizable_finglish(self):
        self.assertEqual(adapter.transcribe_auto(FinglishModel(), "unused.wav", "balanced"), "سلام چطوری")

    def test_prefer_persian_recovers_recognizable_finglish(self):
        self.assertEqual(adapter.transcribe_auto(FinglishModel(), "unused.wav", "prefer-persian"), "سلام چطوری")

    def test_prefer_english_recovers_when_auto_returns_persian_script(self):
        self.assertEqual(adapter.transcribe_auto(PersianAutoModel(), "unused.wav", "prefer-english"), "please open the API settings")

    def test_balanced_auto_recovers_english_when_detector_says_persian_but_script_is_latin(self):
        self.assertEqual(adapter.transcribe_auto(EnglishMisdirectedAsPersianModel(), "unused.wav", "balanced"), "please open the API settings")

    def test_balanced_auto_retries_persian_when_detector_says_english_but_output_is_persian_script(self):
        self.assertEqual(adapter.transcribe_auto(PersianMisdirectedAsEnglishModel(), "unused.wav", "balanced"), "لطفاً تنظیمات را باز کن")

    def test_balanced_auto_preserves_mixed_persian_and_english_technical_terms(self):
        expected = "این API را در Visual Studio باز کن"
        self.assertEqual(adapter.transcribe_auto(MixedScriptModel(), "unused.wav", "balanced"), expected)

    def test_balanced_auto_recovers_low_confidence_finglish_to_persian_script(self):
        model = LowConfidencePersianAsLatinModel()
        self.assertEqual(adapter.transcribe_auto(model, "unused.wav", "balanced"), "این یک آزمایش فارسی است")
        self.assertEqual(model.languages_requested, [None, "fa"])

    def test_balanced_auto_keeps_low_confidence_english_when_persian_comparison_is_weak(self):
        model = LowConfidenceEnglishModel()
        expected = "technical documentation may be confusing"
        self.assertEqual(adapter.transcribe_auto(model, "unused.wav", "balanced"), expected)
        self.assertEqual(model.languages_requested, [None, "fa"])

    def test_mixed_script_output_never_triggers_whole_utterance_retry(self):
        model = MixedScriptMisdirectedModel()
        expected = "این API را در Visual Studio باز کن"
        self.assertEqual(adapter.transcribe_auto(model, "unused.wav", "balanced"), expected)
        self.assertEqual(model.languages_requested, [None])
        self.assertTrue(adapter.has_meaningful_mixed_script(expected))
        self.assertFalse(adapter.has_meaningful_mixed_script("لطفاً تنظیمات را باز کن"))
        self.assertFalse(adapter.has_meaningful_mixed_script("please open the API settings"))


    def test_mixed_mode_recovers_finglish_for_short_audio_too(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "short-mixed.wav"
            make_wav(source, 4)
            self.assertEqual(adapter.transcribe_auto(FinglishModel(), str(source), "mixed-segments"), "سلام چطوری")

    def test_mixed_mode_recovers_recognizable_finglish(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "mixed.wav"
            make_wav(source, 14)
            self.assertEqual(adapter.transcribe_auto(FinglishModel(), str(source), "mixed-segments"), "سلام چطوری")

    def test_mixed_mode_recovers_english_when_a_segment_is_misdirected_as_persian(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "misdirected-english.wav"
            make_wav(source, 14)
            self.assertEqual(adapter.transcribe_auto(EnglishMisdirectedAsPersianModel(), str(source), "mixed-segments"), "please open the API settings")

    def test_mixed_mode_preserves_mixed_language_technical_sentence(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "mixed-technical.wav"
            make_wav(source, 14)
            self.assertEqual(adapter.transcribe_auto(MixedScriptModel(), str(source), "mixed-segments"), "این API را در Visual Studio باز کن")

    def test_technical_term_guard_restores_example_without_translating_sentence(self):
        raw = "لطفاً این ای پی آی را در ویژوال استودیو باز کن و بعد بیلد بگیر"
        expected = "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"
        self.assertEqual(adapter.restore_english_technical_terms(raw), expected)

    def test_technical_term_guard_preserves_urls_email_and_inline_code(self):
        raw = "ویژوال استودیو را باز کن https://example.com/ویژوال و `ویژوال استودیو` و api@example.com"
        expected = "Visual Studio را باز کن https://example.com/ویژوال و `ویژوال استودیو` و api@example.com"
        self.assertEqual(adapter.restore_english_technical_terms(raw), expected)

    def test_technical_term_guard_is_idempotent_and_leaves_clear_english_unchanged(self):
        text = "این API را در Visual Studio باز کن و بعد build بگیر"
        self.assertEqual(adapter.restore_english_technical_terms(adapter.restore_english_technical_terms(text)), text)
        self.assertEqual(adapter.restore_english_technical_terms("Please open Visual Studio and build"), "Please open Visual Studio and build")

    def test_dual_model_hybrid_restores_terms_when_one_model_translates_whole_sentence(self):
        persian_phonetic = "لطفاً این ای پی آی را در ویژوال استودیو باز کن و بعد بیلد بگیر"
        translated_english = "Please open this API in Visual Studio and then build"
        chosen = adapter.choose_dual_model_candidate(
            persian_phonetic, "fa", 0.78,
            translated_english, "en", 0.93,
        )
        self.assertEqual(chosen, "لطفاً این API را در Visual Studio باز کن و بعد build بگیر")

    def test_english_first_does_not_replace_persian_script_when_detection_is_uncertain(self):
        persian = "لطفاً در را باز کن"
        english = "please open the door"
        for preference in ("persian-first", "english-first"):
            self.assertEqual(
                adapter.choose_dual_model_candidate(
                    persian, None, 0.50, english, None, 0.50,
                    transcription_language_preference=preference,
                ),
                persian,
            )

    def test_low_confidence_language_labels_do_not_override_persian_script(self):
        persian = "لطفاً در را باز کن"
        english = "please open the door"
        for preference in ("persian-first", "english-first"):
            self.assertEqual(
                adapter.choose_dual_model_candidate(
                    persian, "fa", 0.52, english, "en", 0.53,
                    transcription_language_preference=preference,
                ),
                persian,
            )

    def test_high_confidence_english_labels_do_not_erase_persian_dominant_mixed_candidate(self):
        mixed_persian = "لطفاً این فایل را Upload کن و در Google Drive ذخیره کن"
        english_translation = "Please upload this file and save it in Google Drive"
        chosen = adapter.choose_dual_model_candidate(
            mixed_persian, "en", 0.91, english_translation, "en", 0.94,
            transcription_language_preference="english-first",
        )
        self.assertEqual(chosen, mixed_persian)

    def test_dual_model_confident_persian_and_mixed_script_outweigh_english_preference(self):
        persian = "سلام دنیا لطفاً در را باز کن"
        english = "hello world please open the door"
        self.assertEqual(
            adapter.choose_dual_model_candidate(
                persian, "fa", 0.91, english, "en", 0.93,
                transcription_language_preference="english-first",
            ),
            persian,
        )
        mixed = "این API را در Visual Studio باز کن"
        translated = "Please open the API in Visual Studio"
        self.assertEqual(
            adapter.choose_dual_model_candidate(
                mixed, "en", 0.80, translated, "en", 0.96,
                transcription_language_preference="english-first",
            ),
            mixed,
        )

    def test_transcription_preference_normalization_defaults_safely(self):
        self.assertEqual(adapter.normalize_transcription_preference("ENGLISH-FIRST"), "english-first")
        self.assertEqual(adapter.normalize_transcription_preference("unexpected"), "persian-first")

    def test_dual_model_term_guard_can_be_disabled(self):
        chosen = adapter.choose_dual_model_candidate(
            "لطفاً این ای پی آی را باز کن", "fa", 0.80,
            "Please open the API", "en", 0.83,
            preserve_english_terms=False,
        )
        self.assertEqual(chosen, "لطفاً این ای پی آی را باز کن")

    def test_dual_mode_splits_short_mixed_utterance_before_comparison(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "short-dual-mixed.wav"
            make_wav(source, 4.2)
            expected = "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"
            phonetic = "لطفاً این ای پی آی را در ویژوال استودیو باز کن و بعد بیلد بگیر"
            fa_model = DualMixedScriptModel(phonetic, "fa", 0.78)
            en_model = DualMixedScriptModel("Please open this API in Visual Studio and then build", "en", 0.92)
            diagnostic = io.StringIO()
            with contextlib.redirect_stderr(diagnostic):
                result = adapter.transcribe_dual_model_mixed(fa_model, en_model, str(source))
            self.assertEqual(result, expected)
            self.assertGreaterEqual(fa_model.calls, 2)
            self.assertEqual(fa_model.calls, en_model.calls)
            self.assertIn("segment 1/", diagnostic.getvalue())

    def test_dual_model_prefers_candidate_that_preserves_mixed_scripts(self):
        expected = "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"
        translated = "Please open this API in Visual Studio"
        chosen = adapter.choose_dual_model_candidate(
            expected, "fa", 0.79,
            translated, "en", 0.91,
        )
        self.assertEqual(chosen, expected)

    def test_dual_model_persian_base_wins_over_longer_english_paraphrase(self):
        expected = "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"
        english_paraphrase = "Please open this API in Visual Studio and then build"
        chosen = adapter.choose_dual_model_candidate(
            expected, "fa", 0.72,
            english_paraphrase, "en", 0.97,
        )
        self.assertEqual(chosen, expected)

    def test_dual_model_preserves_clear_english_only_speech(self):
        expected = "Please open Visual Studio and run the build"
        chosen = adapter.choose_dual_model_candidate(
            "Please open Visual Studio and run the build", "en", 0.91,
            expected, "en", 0.94,
        )
        self.assertEqual(chosen, expected)

    def test_dual_model_uses_english_when_persian_role_translates_clear_english_audio(self):
        expected = "Please open the settings and run the build"
        chosen = adapter.choose_dual_model_candidate(
            "لطفاً تنظیمات را باز کن و build بگیر", "en", 0.89,
            expected, "en", 0.91,
        )
        self.assertEqual(chosen, expected)

    def test_dual_model_preserves_mixed_script_even_if_detector_label_is_english(self):
        expected = "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"
        chosen = adapter.choose_dual_model_candidate(
            expected, "en", 0.81,
            "Please open the API in Visual Studio and then build", "en", 0.95,
        )
        self.assertEqual(chosen, expected)

    def test_dual_model_prefers_confident_english_for_clear_english_segment(self):
        chosen = adapter.choose_dual_model_candidate(
            "open the settings", "en", 0.68,
            "open the settings panel", "en", 0.91,
        )
        self.assertEqual(chosen, "open the settings panel")

    def test_dual_model_prefers_persian_candidate_when_confidence_is_close(self):
        chosen = adapter.choose_dual_model_candidate(
            "لطفاً تنظیمات را باز کن", "fa", 0.80,
            "Please open the settings", "en", 0.83,
        )
        self.assertEqual(chosen, "لطفاً تنظیمات را باز کن")

    def test_dual_model_persian_first_recovers_finglish_before_english_candidate_can_win(self):
        model = MixedFinglishWithUploadModel()
        expected = "امروز باید این فایل رو Upload کنم"
        result = adapter.transcribe_dual_model_mixed(
            model, model, "unused.wav", transcription_language_preference="persian-first"
        )
        self.assertEqual(result, expected)
        self.assertIn("fa", model.languages_requested)

    def test_dual_model_runs_both_models_on_short_overlapping_segments(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "dual-mixed.wav"
            make_wav(source, 10)
            expected = "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"
            fa_model = DualMixedScriptModel(expected, "fa", 0.82)
            en_model = DualMixedScriptModel("Please open this API in Visual Studio", "en", 0.84)
            diagnostic = io.StringIO()
            with contextlib.redirect_stderr(diagnostic):
                result = adapter.transcribe_dual_model_mixed(fa_model, en_model, str(source))
            self.assertEqual(result, expected)
            self.assertGreaterEqual(fa_model.calls, 2)
            self.assertEqual(fa_model.calls, en_model.calls)
            self.assertIn("chosen-model=persian-priority", diagnostic.getvalue())
            self.assertIn("chosen-script=mixed", diagnostic.getvalue())

    def test_dual_model_short_clip_uses_both_models_without_forcing_a_single_language(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "dual-short.wav"
            make_wav(source, 2.2)
            expected = "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"
            fa_model = DualMixedScriptModel(expected, "fa", 0.78)
            en_model = DualMixedScriptModel("Please open this API in Visual Studio", "en", 0.81)
            self.assertEqual(adapter.transcribe_dual_model_mixed(fa_model, en_model, str(source)), expected)
            self.assertEqual(fa_model.calls, 1)
            self.assertEqual(en_model.calls, 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
