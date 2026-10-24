"""Small offline adapter used by MadNegar to call faster-whisper on a local model."""
from __future__ import annotations

import os
import re
import sys
import tempfile
import traceback
import wave
from pathlib import Path
from typing import Iterable


CHUNK_SECONDS = 8
OVERLAP_SECONDS = 0.6
MIN_SEGMENTED_SECONDS = 12
# Mixed speech needs shorter analysis windows than ordinary dictation. Otherwise a
# 4-6 second Persian sentence containing English product/technical names is treated
# as one language and one entire-model transcript wins.
DUAL_MODEL_CHUNK_SECONDS = 2.8
DUAL_MODEL_OVERLAP_SECONDS = 0.55
DUAL_MODEL_MIN_SEGMENTED_SECONDS = 2.7
MIXED_MODE_CHUNK_SECONDS = 3.8
MIXED_MODE_OVERLAP_SECONDS = 0.55
MIXED_MODE_MIN_SEGMENTED_SECONDS = 3.6

# Conservative local canonicalization for common technical names/terms that Whisper
# sometimes writes phonetically in Persian script. Only whole aliases are replaced;
# this never translates the surrounding sentence. Users can add domain-specific items
# in the Local Dictionary settings.
ENGLISH_TECHNICAL_TERM_ALIASES: tuple[tuple[str, tuple[str, ...]], ...] = (
    ("Visual Studio Code", ("ویژوال استودیو کد", "ویژوال‌استودیو کد", "ویژوال استودیوکد")),
    ("Visual Studio", ("ویژوال استودیو", "ویژوال‌استودیو", "ویژوال استادیو", "ویژوال‌استادیو")),
    ("faster-whisper", ("فستر ویسپر", "فستر‌ویسپر", "فسترویسپر")),
    ("JavaScript", ("جاوا اسکریپت", "جاوااسکریپت", "جاوا‌اسکریپت")),
    ("TypeScript", ("تایپ اسکریپت", "تایپ‌اسکریپت")),
    ("PowerShell", ("پاور شل", "پاورشل", "پاور‌شل")),
    ("PowerPoint", ("پاور پوینت", "پاورپوینت", "پاور‌پوینت")),
    ("Node.js", ("نود جی اس", "نود‌جی‌اس", "نود جی‌اس")),
    (".NET Core", ("دات نت کور", "دات‌نت کور", "دات نت‌کور", "دات‌نت‌کور")),
    (".NET", ("دات نت", "دات‌نت")),
    ("VS Code", ("وی اس کد", "وی‌اس کد", "وی اس‌کد", "وی‌اس‌کد")),
    ("ChatGPT", ("چت جی پی تی", "چت‌جی‌پی‌تی", "چت جی‌پی‌تی", "چت‌جی پی تی")),
    ("GitHub", ("گیت هاب", "گیت‌هاب", "گیت‌ هاب")),
    ("GitLab", ("گیت لب", "گیت‌لب")),
    ("Java", ("جاوا",)),
    ("Python", ("پایتون",)),
    ("Docker", ("داکر",)),
    ("Kubernetes", ("کوبرنتیز", "کوبِرنتیس", "کابرنتیز")),
    ("React", ("ری اکت", "ری‌اکت", "ریاکت")),
    ("Vue.js", ("ویو جی اس", "ویو‌جی‌اس", "ویو جی‌اس")),
    ("Git", ("گیت",)),
    ("API", ("ای پی آی", "ای‌پی‌آی", "ای پی‌آی", "ای‌ پی آی", "ای پی ای")),
    ("C++", ("سی پلاس پلاس", "سی‌پلاس‌پلاس")),
    ("C#", ("سی شارپ", "سی‌شارپ")),
    ("JSON", ("جی سان", "جی‌سان", "جیسون")),
    ("XML", ("ایکس ام ال", "ایکس‌ام‌ال")),
    ("HTML", ("اچ تی ام ال", "اچ‌تی‌ام‌ال")),
    ("CSS", ("سی اس اس", "سی‌اس‌اس")),
    ("SQL", ("اس کیو ال", "اس‌کیو‌ال", "اس‌کیو‌ال")),
    ("Notepad", ("نوت پد", "نوت‌پد")),
    ("Windows", ("ویندوز",)),
    ("Whisper", ("ویسپر",)),
    ("NuGet", ("نیو گت", "نیوگت")),
    ("FFmpeg", ("اف اف امپگ", "اف‌اف‌امپگ")),
    ("OpenAI", ("اوپن ای آی", "اوپن‌ای‌آی")),
    ("Google Drive", ("گوگل درایو", "گوگل‌درایو", "گوگلدرایو")),
    ("Upload", ("آپلود", "آپ‌لود", "اپلود")),
    ("Download", ("دانلود", "دان‌لود")),
    ("Update", ("آپدیت", "آپ‌دیت", "اپدیت")),
    ("Install", ("اینستال", "این‌استال")),
    ("Login", ("لاگین", "لاگ این", "لاگ‌این")),
    ("Logout", ("لاگ‌اوت", "لاگ اوت", "لاگوت")),
    ("build", ("بیلد", "بایلد")),
    ("debug", ("دیباگ",)),
    ("compile", ("کامپایل",)),
    ("deploy", ("دیپلوی",)),
    ("runtime", ("ران تایم", "ران‌تایم")),
)

_PROTECTED_TEXT_RE = re.compile(r"https?://\S+|www\.\S+|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|`[^`]*`", re.IGNORECASE)


def restore_english_technical_terms(text: str) -> str:
    """Canonicalize a small set of common English tech terms mis-rendered phonetically.

    This is not translation or a general-purpose language detector. It replaces exact
    whole aliases only and leaves URLs, email addresses, and inline-code spans untouched.
    """
    if not text or not text.strip():
        return text

    def normalize_unprotected(part: str) -> str:
        result = part
        for canonical, aliases in ENGLISH_TECHNICAL_TERM_ALIASES:
            for alias in sorted(aliases, key=len, reverse=True):
                words = alias.replace("\u200c", " ").split()
                if not words:
                    continue
                separator = r"[\s\u200c]+"
                # ZWNJ joins Persian compound words; do not restore only a technical-looking prefix.
                pattern = r"(?<![\w\u200c])" + separator.join(re.escape(word) for word in words) + r"(?![\w\u200c])"
                result = re.sub(pattern, canonical, result, flags=re.IGNORECASE)
        return result

    spans = _PROTECTED_TEXT_RE.split(text)
    protected = _PROTECTED_TEXT_RE.findall(text)
    if not protected:
        return normalize_unprotected(text)
    output: list[str] = []
    for index, span in enumerate(spans):
        output.append(normalize_unprotected(span))
        if index < len(protected):
            output.append(protected[index])
    return "".join(output)


def configure_utf8_stdio() -> None:
    """Force UTF-8 for redirected Windows stdout/stderr, preserving Persian text."""
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        if stream is None:
            continue
        try:
            stream.reconfigure(encoding="utf-8", errors="backslashreplace")
        except (AttributeError, OSError, ValueError):
            pass


def write_utf8_stdout(text: str) -> None:
    """Write the transcript as explicit UTF-8 bytes when possible."""
    payload = (text + "\n").encode("utf-8", errors="replace")
    binary = getattr(sys.stdout, "buffer", None)
    if binary is not None:
        binary.write(payload)
        binary.flush()
        return
    sys.stdout.write(payload.decode("utf-8"))
    sys.stdout.flush()


def contains_persian_script(text: str) -> bool:
    return any("\u0600" <= char <= "\u06ff" or "\u0750" <= char <= "\u077f"
               or "\u08a0" <= char <= "\u08ff" or "\ufb50" <= char <= "\ufdff"
               or "\ufe70" <= char <= "\ufeff" for char in text)


def count_persian_letters(text: str) -> int:
    return sum(char.isalpha() and ("\u0600" <= char <= "\u06ff" or "\u0750" <= char <= "\u077f"
                                   or "\u08a0" <= char <= "\u08ff" or "\ufb50" <= char <= "\ufdff"
                                   or "\ufe70" <= char <= "\ufeff") for char in text)


def count_latin_letters(text: str) -> int:
    return sum(char.isalpha() and ("a" <= char.lower() <= "z") for char in text)


def normalize_token(token: str) -> str:
    return "".join(char.lower() for char in token if char.isalnum())


def looks_like_romanized_persian(text: str) -> bool:
    if not text.strip() or contains_persian_script(text):
        return False
    latin = count_latin_letters(text)
    letters = sum(char.isalpha() for char in text)
    if latin < 4 or latin * 100 // max(1, letters) < 70:
        return False
    words = {word for word in re.split(r"[^a-z0-9]+", text.lower()) if word}
    distinctive = {
        "salam", "salaam", "chetori", "chetor", "chejuri", "chejory", "mikham",
        "mikhastam", "mikhay", "mishe", "mitoonam", "nemitoonam", "nemidunam",
        "nemidoonam", "khoobam", "khoobi", "khubam", "khobi", "khodafez", "baleh",
        "lotfan", "emrooz", "emruz", "khune", "khoone", "chera", "doroste", "dorost",
        "fekr", "daram", "dari", "hastam", "nistam", "khodam", "baram", "behem",
        "mamnoon", "khoshhal", "mikoni", "khabar", "emrooz", "emruz", "bayad",
        "konam", "konim", "konid", "kardan", "kardam", "kardi", "karde", "kard",
        "bokon", "begu", "begoo", "bego", "beshin", "berim", "beram", "beran",
        "inja", "oonja", "unja", "koja", "chon", "chonke", "vaghti", "alan", "hala",
        "ghablan", "badesh", "khahesh", "injoori", "hamishe", "hamechi", "migan",
        "migam", "mige", "migim", "mishan", "misham", "nemishe", "nemikham", "nemikhad",
        "nemikonam", "nemikonid", "midunam", "midonam", "azmayesh", "azmoon", "zabane",
        "zaban", "goftam", "gofti", "goftan", "shode", "misheh", "ast", "hast", "hastesh",
        "nisti", "nist", "hastin", "khuneh", "dige", "digar", "haminjoori", "baraye",
        "bara", "baad", "sobh", "shab"
    }
    return bool(words & distinctive) or ("farsi" in words and "man" in words)


def merge_segment_texts(texts: Iterable[str]) -> str:
    """Merge overlapping speech chunks without rewriting words or inventing missing text."""
    merged: list[str] = []
    for raw in texts:
        tokens = re.split(r"\s+", raw.strip()) if raw and raw.strip() else []
        tokens = [token for token in tokens if token]
        max_overlap = min(18, len(merged), len(tokens))
        overlap = 0
        for candidate in range(max_overlap, 0, -1):
            previous = [normalize_token(token) for token in merged[-candidate:]]
            upcoming = [normalize_token(token) for token in tokens[:candidate]]
            if all(left and right and left == right for left, right in zip(previous, upcoming)):
                overlap = candidate
                break
        merged.extend(tokens[overlap:])
    return " ".join(merged).strip()


def split_wave_file(
    audio_path: str,
    output_directory: str,
    chunk_seconds: float = CHUNK_SECONDS,
    overlap_seconds: float = OVERLAP_SECONDS,
    min_segmented_seconds: float = MIN_SEGMENTED_SECONDS,
) -> list[str]:
    """Split PCM WAV into overlapping chunks. Return [audio_path] for short/unsupported audio."""
    try:
        with wave.open(audio_path, "rb") as source:
            params = source.getparams()
            if params.comptype != "NONE" or params.sampwidth != 2 or params.nchannels != 1:
                return [audio_path]
            if params.framerate <= 0 or params.nframes / params.framerate <= min_segmented_seconds:
                return [audio_path]
            chunk_frames = max(1, int(params.framerate * chunk_seconds))
            overlap_frames = max(0, int(params.framerate * overlap_seconds))
            step_frames = max(1, chunk_frames - overlap_frames)
            total_frames = params.nframes
            Path(output_directory).mkdir(parents=True, exist_ok=True)
            paths: list[str] = []
            start = 0
            while start < total_frames:
                frame_count = min(chunk_frames, total_frames - start)
                source.setpos(start)
                data = source.readframes(frame_count)
                if not data:
                    break
                path = os.path.join(output_directory, f"segment-{len(paths) + 1:03d}.wav")
                with wave.open(path, "wb") as target:
                    target.setnchannels(params.nchannels)
                    target.setsampwidth(params.sampwidth)
                    target.setframerate(params.framerate)
                    target.setcomptype(params.comptype, params.compname)
                    target.writeframes(data)
                paths.append(path)
                if start + frame_count >= total_frames:
                    break
                start += step_frames
            return paths or [audio_path]
    except (OSError, EOFError, wave.Error, ValueError):
        return [audio_path]


MIXED_LANGUAGE_TRANSCRIPTION_PROMPT = (
    "Transcribe the exact spoken words without translating. Speech may contain Persian and English "
    "in the same sentence. Write Persian words using Persian script and genuine English words using "
    "Latin letters. Do not transliterate either language into the other's writing system. "
    "Do not paraphrase, translate, or omit either language. متن فارسی را با خط فارسی بنویس؛ "
    "واژه‌های واقعاً انگلیسی را با حروف لاتین نگه دار."
)
# Both preferences must preserve the writing system of the words actually spoken. The preference
# only breaks uncertain model-selection ties; changing it must not bias single-model Auto toward
# an English or Persian rendering of the whole utterance.
PERSIAN_FIRST_TRANSCRIPTION_PROMPT = MIXED_LANGUAGE_TRANSCRIPTION_PROMPT
ENGLISH_FIRST_TRANSCRIPTION_PROMPT = MIXED_LANGUAGE_TRANSCRIPTION_PROMPT

def transcribe_with_model(
    model, audio_path: str, language: str | None, condition_previous: bool = True,
    transcription_language_preference: str | None = None,
) -> tuple[str, str | None, float | None]:
    kwargs = dict(
        language=language,
        beam_size=5,
        vad_filter=True,
        condition_on_previous_text=condition_previous,
    )
    # faster-whisper has no hard mixed-script guarantee. These prompts encourage each language to
    # retain its own writing system; the preference changes ambiguous-language guidance only.
    if language is None and transcription_language_preference is not None:
        preference = normalize_transcription_preference(transcription_language_preference)
        kwargs["initial_prompt"] = (
            ENGLISH_FIRST_TRANSCRIPTION_PROMPT if preference == "english-first"
            else PERSIAN_FIRST_TRANSCRIPTION_PROMPT
        )
    segments, info = model.transcribe(audio_path, **kwargs)
    text = " ".join(segment.text.strip() for segment in segments if segment.text.strip()).strip()
    detected = getattr(info, "language", None)
    probability = getattr(info, "language_probability", None)
    return text, detected, probability


def is_english_candidate(candidate: str, baseline: str) -> bool:
    """Accept an English retry only when its script and length are plausible, not on language label alone."""
    latin = count_latin_letters(candidate)
    persian = count_persian_letters(candidate)
    baseline_letters = count_persian_letters(baseline) + count_latin_letters(baseline)
    candidate_letters = persian + latin
    return (
        latin >= 4
        and persian <= latin // 3
        and candidate_letters >= max(4, int(baseline_letters * 0.45))
        and len(candidate.strip()) >= max(4, int(len(baseline.strip()) * 0.45))
    )


def has_meaningful_mixed_script(text: str) -> bool:
    """Return true when both Persian and Latin words are substantial parts of an utterance.

    This avoids forcing a code-switched sentence wholly into one language just because
    the model's global language label disagrees with the dominant script.
    """
    persian = count_persian_letters(text)
    latin = count_latin_letters(text)
    total = persian + latin
    return total >= 10 and persian >= 3 and latin >= 3 and persian * 100 // total >= 12 and latin * 100 // total >= 12


def recover_auto_script_mismatch(model, audio_path: str, text: str, detected: str | None,
                                 probability: float | None) -> str:
    """Conservatively retry a clear script/language mismatch while preserving ordinary code-switching."""
    if looks_like_romanized_persian(text):
        persian, _language, _confidence = transcribe_with_model(model, audio_path, "fa")
        if count_persian_letters(persian) >= 2 and len(persian) >= max(4, int(len(text) * 0.45)):
            return persian
        return text

    if has_meaningful_mixed_script(text):
        return text

    persian_letters = count_persian_letters(text)
    latin_letters = count_latin_letters(text)
    total_letters = persian_letters + latin_letters
    mismatch = (
        detected == "fa" and persian_letters == 0 and latin_letters >= 4
    ) or (
        detected == "en" and total_letters >= 8 and persian_letters >= 6
        and persian_letters * 100 // max(1, total_letters) >= 70
    )
    # For genuinely low-confidence, Latin-only results, perform one carefully gated Persian
    # comparison. This catches some Persian utterances that Whisper translates/transliterates
    # into fluent Latin text even though its top language label is English. Never do this on
    # clear/high-confidence English or on sentences containing meaningful content in both scripts.
    low_confidence_persian_check = (
        not mismatch
        and detected == "en"
        and probability is not None
        and probability < 0.40
        and persian_letters == 0
        and latin_letters >= 8
    )
    if not mismatch and not low_confidence_persian_check:
        return text

    retry_language = "en" if detected == "fa" else "fa"
    candidate, retry_detected, retry_probability = transcribe_with_model(model, audio_path, retry_language)
    if retry_language == "en":
        candidate_ok = is_english_candidate(candidate, text)
        script_ok = retry_detected in (None, "en") and (retry_probability is None or retry_probability >= 0.45)
    else:
        candidate_ok = count_persian_letters(candidate) >= 2 and len(candidate) >= max(4, int(len(text) * 0.45))
        if low_confidence_persian_check:
            # For the uncertain-detector-only path, require positive evidence from the comparison
            # pass rather than accepting the language merely because fa was forced as input.
            script_ok = retry_detected == "fa" and retry_probability is not None and retry_probability >= 0.55
        else:
            script_ok = retry_detected in (None, "fa") and (retry_probability is None or retry_probability >= 0.45)
    # When detector confidence is already high in the opposite direction, require a valid retry.
    # This is a bounded second pass, not a claim that script counts prove speech language.
    if candidate_ok and script_ok:
        print(f"[MadNegar Auto] script/language mismatch recovery tried={retry_language} original_confidence={probability if probability is not None else 'n/a'}", file=sys.stderr)
        return candidate
    return text


def recover_persian_preferred_script(model, audio_path: str, text: str, detected: str | None,
                                     probability: float | None) -> str:
    """Correct recognizable Finglish into Persian script without rewriting clear English.

    Uncertain Latin-only transcripts may get one Persian comparison, but that generic fallback is
    accepted only with language-detector confirmation. Recognizable Finglish is gated by lexical
    clues plus a substantial Persian-script retry.
    """
    if not text or contains_persian_script(text):
        return text
    latin = count_latin_letters(text)
    if latin < 6:
        return text
    recognizable_finglish = looks_like_romanized_persian(text)
    uncertain_latin_only = (
        latin >= 8
        and (
            detected not in {"en", "english", "fa", "persian"}
            or probability is None
            or (probability is not None and 0.40 <= probability < 0.60)
        )
    )
    if not recognizable_finglish and not uncertain_latin_only:
        return text

    candidate, retry_detected, retry_probability = transcribe_with_model(model, audio_path, "fa")
    candidate_persian = count_persian_letters(candidate)
    baseline_letters = max(1, latin)
    candidate_letters = candidate_persian + count_latin_letters(candidate)
    candidate_ok = (
        candidate_persian >= 3
        and candidate_letters >= max(4, int(baseline_letters * 0.45))
        and len(candidate.strip()) >= max(4, int(len(text.strip()) * 0.45))
        and candidate_persian * 100 // max(1, candidate_letters) >= 45
    )
    if recognizable_finglish:
        # Forced-language faster-whisper calls may not return language_probability.
        retry_ok = candidate_ok and retry_detected in (None, "fa", "persian")
    else:
        # Do not turn a weak, genuinely-English sentence into Persian just because a forced-fa
        # decoder can produce text. Without clear Finglish cues, require detector confirmation.
        retry_ok = (
            candidate_ok
            and retry_detected in ("fa", "persian")
            and retry_probability is not None
            and retry_probability >= 0.55
        )
    if retry_ok:
        print("[MadNegar Auto] Persian-script recovery accepted", file=sys.stderr)
        return candidate
    return text


def _script_class(text: str) -> str:
    """Coarse script signal only; this is not a claim of semantic language certainty."""
    if has_meaningful_mixed_script(text):
        return "mixed"
    persian = count_persian_letters(text)
    latin = count_latin_letters(text)
    total = persian + latin
    if looks_like_romanized_persian(text):
        return "finglish"
    if persian >= 3 and persian * 100 // max(1, total) >= 55:
        return "persian"
    if latin >= 4 and latin * 100 // max(1, total) >= 65:
        return "english"
    if persian:
        return "persian"
    if latin:
        return "english"
    return "unknown"


def normalize_transcription_preference(value: str | None) -> str:
    return "english-first" if (value or "").strip().lower() == "english-first" else "persian-first"


def choose_dual_model_candidate(
    persian_text: str, persian_detected: str | None, persian_probability: float | None,
    english_text: str, english_detected: str | None, english_probability: float | None,
    preserve_english_terms: bool = True,
    transcription_language_preference: str = "persian-first",
) -> str:
    """Choose a candidate using the recognizer's raw text, then restore known terms afterward.

    Script and language scoring must use the raw model outputs. Canonicalizing a Persian-rendered
    technical word (for example, "آپلود" -> "Upload") before scoring can make an otherwise Persian
    translation look like a genuine mixed-script transcript and hide an English translation error.
    """
    p_text = (persian_text or "").strip()
    e_text = (english_text or "").strip()
    p_output = restore_english_technical_terms(p_text) if preserve_english_terms else p_text
    e_output = restore_english_technical_terms(e_text) if preserve_english_terms else e_text
    if not p_text:
        return e_output
    if not e_text:
        return p_output

    p_class = _script_class(p_text)
    e_class = _script_class(e_text)
    p_lang = (persian_detected or "").strip().lower()
    e_lang = (english_detected or "").strip().lower()
    p_prob = persian_probability if persian_probability is not None else 0.5
    e_prob = english_probability if english_probability is not None else 0.5
    p_has_persian = count_persian_letters(p_text) >= 3
    e_has_persian = count_persian_letters(e_text) >= 3
    p_latin = count_latin_letters(p_text)
    e_latin = count_latin_letters(e_text)
    p_looks_english = p_latin >= 4 and not p_has_persian
    e_looks_english = e_latin >= 4 and not e_has_persian
    p_reported_english = p_lang in {"en", "english"} and p_prob >= 0.55
    e_reported_english = e_lang in {"en", "english"} and e_prob >= 0.55
    p_reported_persian = p_lang in {"fa", "persian"} and p_prob >= 0.45
    e_reported_persian = e_lang in {"fa", "persian"} and e_prob >= 0.45

    # A Persian-role model may translate genuinely English audio into Persian script. Only trust
    # the English comparison here when the raw Persian candidate is not actually code-switched.
    # Do not let post-processing of a technical term affect this decision.
    p_persian_share = count_persian_letters(p_text) / max(
        1, count_persian_letters(p_text) + p_latin
    )
    # A single isolated Latin technical token in otherwise Persian text (for example, a
    # translated candidate ending in "build") is not enough to prove genuine code-switching.
    # Several Latin tokens alongside Persian script are stronger evidence of the user's intended
    # mixed-language sentence and must be preserved even when a global detector says English.
    raw_latin_words = re.findall(r"(?<![A-Za-z])[A-Za-z][A-Za-z0-9.+#-]*(?![A-Za-z])", p_text)
    robust_mixed_candidate = has_meaningful_mixed_script(p_text) and len(raw_latin_words) >= 2
    if (not robust_mixed_candidate
            and p_reported_english and p_prob >= 0.75 and p_has_persian and p_persian_share >= 0.72
            and e_looks_english and e_reported_english and e_prob >= 0.65):
        return e_output

    # An already code-switched raw transcript preserves both parts and normally wins over a
    # one-language paraphrase, even if the model's language label is wrong.
    if p_class == "mixed":
        return p_output
    if e_class == "mixed":
        return e_output

    if p_reported_persian and p_prob >= 0.65 and p_has_persian:
        return p_output
    if e_reported_persian and e_prob >= 0.65 and e_has_persian and not p_has_persian:
        return e_output

    # A role-labeled engine can still yield the most faithful candidate in the other language;
    # detector/script agreement takes priority over model role.
    if p_reported_english and p_looks_english and (not e_looks_english or p_prob >= e_prob + 0.12):
        return p_output
    if e_reported_english and e_looks_english and (not p_looks_english or e_prob >= p_prob - 0.08):
        return e_output

    p_agrees = (p_class == "persian" and p_lang in {"fa", "persian"}) or (p_class == "english" and p_lang in {"en", "english"})
    e_agrees = (e_class == "persian" and e_lang in {"fa", "persian"}) or (e_class == "english" and e_lang in {"en", "english"})
    p_score = p_prob + (0.18 if p_agrees else -0.15)
    e_score = e_prob + (0.18 if e_agrees else -0.15)
    if p_has_persian:
        p_score += 0.08
    if e_has_persian:
        e_score += 0.08

    preference = normalize_transcription_preference(transcription_language_preference)
    detection_is_uncertain = not (
        (p_reported_persian and p_prob >= 0.65)
        or (e_reported_persian and e_prob >= 0.65)
        or (p_reported_english and p_prob >= 0.65)
        or (e_reported_english and e_prob >= 0.65)
    )
    if p_class == "persian" and e_class == "english":
        # Preference cannot replace a Persian sentence with an English paraphrase when detectors
        # are uncertain. Clear, confident detection can still select the English candidate.
        if detection_is_uncertain:
            return p_output
        return p_output if p_score >= e_score + 0.10 else e_output
    if p_class == "english" and e_class == "persian":
        if detection_is_uncertain:
            return e_output
        return e_output if e_score >= p_score + 0.10 else p_output
    if abs(p_score - e_score) <= 0.025:
        # The preference only breaks a residual tie after candidate script class has not resolved it.
        if detection_is_uncertain and preference == "english-first" and e_class == "english":
            return e_output
        if detection_is_uncertain and preference == "persian-first" and p_class == "persian":
            return p_output
        return p_output if len(p_text) >= len(e_text) * 0.70 else e_output
    return p_output if p_score > e_score else e_output


def transcribe_dual_model_mixed(
    persian_priority_model, english_comparison_model, audio_path: str,
    preserve_english_terms: bool = True,
    transcription_language_preference: str = "persian-first",
) -> str:
    """Run two local faster-whisper models on short overlapping chunks and pick the best script-preserving candidate."""
    with tempfile.TemporaryDirectory(prefix="madnegar-dual-model-") as temp_dir:
        chunks = split_wave_file(
            audio_path,
            temp_dir,
            chunk_seconds=DUAL_MODEL_CHUNK_SECONDS,
            overlap_seconds=DUAL_MODEL_OVERLAP_SECONDS,
            min_segmented_seconds=DUAL_MODEL_MIN_SEGMENTED_SECONDS,
        )
        results: list[str] = []
        for index, chunk_path in enumerate(chunks, start=1):
            preference = normalize_transcription_preference(transcription_language_preference)
            p_text, p_detected, p_probability = transcribe_with_model(
                persian_priority_model, chunk_path, None, condition_previous=False,
                transcription_language_preference=preference,
            )
            e_text, e_detected, e_probability = transcribe_with_model(
                english_comparison_model, chunk_path, None, condition_previous=False,
                transcription_language_preference=preference,
            )
            recovered_persian = recover_persian_preferred_script(
                persian_priority_model, chunk_path, p_text, p_detected, p_probability
            )
            persian_script_recovery_used = recovered_persian != p_text
            if persian_script_recovery_used:
                # When the Persian-role decoder's output is recognizable Finglish, don't let a fluent
                # English paraphrase from the comparison model override the corrected Persian transcript.
                chosen = recovered_persian
                if preserve_english_terms:
                    chosen = restore_english_technical_terms(chosen)
            else:
                chosen = choose_dual_model_candidate(
                    p_text, p_detected, p_probability,
                    e_text, e_detected, e_probability,
                    preserve_english_terms=preserve_english_terms,
                    transcription_language_preference=preference,
                )
            if persian_script_recovery_used:
                chosen_source = "persian-script-recovery"
            elif p_text == e_text:
                chosen_source = "both-identical"
            elif chosen == p_text:
                chosen_source = "persian-priority"
            else:
                chosen_source = "english-comparison"
            print(
                f"[MadNegar Dual Auto] segment {index}/{len(chunks)} "
                f"fa-model={p_detected or 'unknown'}:{p_probability if p_probability is not None else 'n/a'} "
                f"en-model={e_detected or 'unknown'}:{e_probability if e_probability is not None else 'n/a'} "
                f"chosen-model={chosen_source} chosen-script={_script_class(chosen)}",
                file=sys.stderr,
            )
            results.append(chosen)
        combined = merge_segment_texts(results)
        return restore_english_technical_terms(combined) if preserve_english_terms else combined


def transcribe_auto(model, audio_path: str, strategy: str,
                    transcription_language_preference: str = "persian-first") -> str:
    # A single model exposes no safe word-level language-preference API. Keep Auto detection and
    # conservative script recovery here; the preference is used for ambiguous dual-model choices.
    transcription_language_preference = normalize_transcription_preference(transcription_language_preference)
    if strategy == "mixed-segments":
        with tempfile.TemporaryDirectory(prefix="madnegar-auto-language-") as temp_dir:
            chunks = split_wave_file(
                audio_path, temp_dir,
                chunk_seconds=MIXED_MODE_CHUNK_SECONDS,
                overlap_seconds=MIXED_MODE_OVERLAP_SECONDS,
                min_segmented_seconds=MIXED_MODE_MIN_SEGMENTED_SECONDS,
            )
            if len(chunks) > 1:
                results: list[str] = []
                for index, chunk_path in enumerate(chunks, start=1):
                    text, detected, probability = transcribe_with_model(
                        model, chunk_path, None, condition_previous=False,
                        transcription_language_preference=transcription_language_preference,
                    )
                    print(f"[MadNegar Auto] segment {index}/{len(chunks)} detected={detected or 'unknown'} confidence={probability if probability is not None else 'n/a'}", file=sys.stderr)
                    preferred_text = recover_persian_preferred_script(model, chunk_path, text, detected, probability)
                    if preferred_text != text:
                        text = preferred_text
                    else:
                        text = recover_auto_script_mismatch(model, chunk_path, text, detected, probability)
                    results.append(text)
                combined = merge_segment_texts(results)
                if combined:
                    if looks_like_romanized_persian(combined):
                        persian, _lang, _prob = transcribe_with_model(model, audio_path, "fa")
                        if count_persian_letters(persian) >= 2 and len(persian) >= max(4, int(len(combined) * 0.45)):
                            return persian
                    return combined

    automatic, detected, probability = transcribe_with_model(
        model, audio_path, None, transcription_language_preference=transcription_language_preference
    )
    print(f"[MadNegar Auto] detected={detected or 'unknown'} confidence={probability if probability is not None else 'n/a'}", file=sys.stderr)

    preferred_text = recover_persian_preferred_script(model, audio_path, automatic, detected, probability)
    if preferred_text != automatic:
        return preferred_text

    if strategy == "prefer-persian":
        # Do not force Persian for a clear English transcript. Retry when the script looks like
        # recognizable Finglish, or the language detector itself was uncertain/misdirected.
        uncertain_language = detected != "en" or probability is None or probability < 0.60
        if looks_like_romanized_persian(automatic) or (
            uncertain_language and not contains_persian_script(automatic) and count_latin_letters(automatic) >= 4
        ):
            persian, _lang, _prob = transcribe_with_model(model, audio_path, "fa")
            if count_persian_letters(persian) >= 2 and len(persian) >= max(4, int(len(automatic) * 0.45)):
                return persian
    elif strategy == "prefer-english":
        persian_count = count_persian_letters(automatic)
        latin_count = count_latin_letters(automatic)
        if persian_count + latin_count >= 8 and persian_count >= 6 and persian_count * 100 // max(1, persian_count + latin_count) >= 70:
            english, _lang, _prob = transcribe_with_model(model, audio_path, "en")
            if count_latin_letters(english) >= 4 and count_persian_letters(english) <= count_latin_letters(english) // 3 and len(english) >= max(4, int(len(automatic) * 0.45)):
                return english
    elif strategy in {"balanced", "mixed-segments"}:
        # Recover clear script/detector mismatches in either direction. Mixed-script outputs
        # without a strong mismatch are left untouched to protect English technical terms.
        return recover_auto_script_mismatch(model, audio_path, automatic, detected, probability)
    return automatic


def main() -> int:
    configure_utf8_stdio()

    if len(sys.argv) < 4:
        print("Usage: FasterWhisperAdapter.py MODEL_DIR AUDIO_WAV LANGUAGE [AUTO_STRATEGY] [ENGLISH_MODEL_DIR] [PRESERVE_ENGLISH_TERMS] [TRANSCRIPTION_LANGUAGE_PREFERENCE]", file=sys.stderr)
        return 2

    model_dir, audio_path, language = sys.argv[1:4]
    strategy = sys.argv[4].strip().lower() if len(sys.argv) > 4 else "balanced"
    english_model_dir = sys.argv[5] if len(sys.argv) > 5 and sys.argv[5].strip() else None
    preserve_english_terms = len(sys.argv) <= 6 or sys.argv[6].strip().lower() not in {"0", "false", "off", "no"}
    transcription_language_preference = normalize_transcription_preference(sys.argv[7] if len(sys.argv) > 7 else None)
    if strategy not in {"balanced", "prefer-persian", "prefer-english", "mixed-segments", "dual-model-mixed"}:
        strategy = "balanced"
    model_file = os.path.join(model_dir, "model.bin")
    if not os.path.isfile(model_file):
        print(f"The downloaded faster-whisper model is incomplete: missing {model_file}", file=sys.stderr)
        return 3
    if not os.path.isfile(audio_path):
        print(f"Audio file not found: {audio_path}", file=sys.stderr)
        return 4

    try:
        from faster_whisper import WhisperModel

        cpu_threads = max(1, (os.cpu_count() or 2) - 1)
        def load_model(directory: str):
            model_file = os.path.join(directory, "model.bin")
            config_file = os.path.join(directory, "config.json")
            tokenizer_files = ("tokenizer.json", "vocabulary.json", "vocabulary.txt")
            if not os.path.isfile(model_file):
                raise RuntimeError(f"The downloaded faster-whisper model is incomplete: missing {model_file}")
            if not os.path.isfile(config_file):
                raise RuntimeError(f"The downloaded faster-whisper model is incomplete: missing {config_file}")
            if not any(os.path.isfile(os.path.join(directory, name)) for name in tokenizer_files):
                raise RuntimeError(f"The downloaded faster-whisper model is incomplete: missing tokenizer/vocabulary assets under {directory}")
            return WhisperModel(
                directory,
                device="cpu",
                compute_type="int8",
                cpu_threads=cpu_threads,
                local_files_only=True,
            )

        model = load_model(model_dir)
        selected_language = None if language.lower() in ("", "auto", "none") else language
        if selected_language is None and strategy == "dual-model-mixed":
            if not english_model_dir:
                raise RuntimeError("Dual-model mixed Auto needs an English comparison model directory. Select two downloaded, different faster-whisper models in Settings → Transcription.")
            if os.path.abspath(model_dir).casefold() == os.path.abspath(english_model_dir).casefold():
                raise RuntimeError("Dual-model mixed Auto needs two different faster-whisper model IDs. Choose different Persian-priority and English-comparison models.")
            # Dual mode deliberately keeps both selected models alive for the current job, avoiding
            # model reloads for every short segment. It can use substantially more RAM than single-model mode.
            english_model = load_model(english_model_dir)
            text = transcribe_dual_model_mixed(
                model, english_model, audio_path,
                preserve_english_terms=preserve_english_terms,
                transcription_language_preference=transcription_language_preference,
            )
        elif selected_language is None:
            text = transcribe_auto(model, audio_path, strategy, transcription_language_preference)
        else:
            text, _detected, _probability = transcribe_with_model(model, audio_path, selected_language)
        if selected_language is None and preserve_english_terms:
            text = restore_english_technical_terms(text)
        write_utf8_stdout(text)
        return 0
    except Exception:
        traceback.print_exc(file=sys.stderr)
        return 10


if __name__ == "__main__":
    raise SystemExit(main())
