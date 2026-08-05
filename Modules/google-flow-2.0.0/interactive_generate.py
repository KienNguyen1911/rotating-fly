#!/usr/bin/env python3
"""
سكربت توليد الصور التفاعلي القائم على القائمة لـ Flow (يدعم العربية/الإنجليزية/ثنائية اللغة).
"""

import asyncio
import sys
import time
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from google_flow import ImageGenerator, get_config
from google_flow.models import DEFAULT_MODEL, IMAGE_MODELS

ASPECT_OPTIONS: List[Tuple[str, str, str]] = [
    ("landscape", "أفقي 16:9", "Landscape 16:9"),
    ("portrait", "عمودي 9:16", "Portrait 9:16"),
    ("square", "مربع 1:1", "Square 1:1"),
    ("four-three", "أفقي 4:3", "Landscape 4:3"),
    ("three-four", "عمودي 3:4", "Portrait 3:4"),
]

# أبعاد الصورة الافتراضية 16:9
DEFAULT_ASPECT = "landscape"

# خيارات الدقة، none في البداية كخيار افتراضي
RESOLUTION_OPTIONS: List[Tuple[str, str, str]] = [
    ("none", "الصورة الأصلية", "Original"),
    ("2k", "2K", "2K"),
    ("4k", "4K", "4K"),
]

# الدقة الافتراضية (عرض الصورة الأصلية)
DEFAULT_RESOLUTION = "none"

LANGUAGE_OPTIONS: List[Tuple[str, str]] = [
    ("ar", "العربية"),
    ("en", "English"),
    ("bi", "ثنائية اللغة / Bilingual"),
]

DEFAULT_OUTPUT_TEMPLATE = "output/flow_{timestamp}.png"


class InputClosed(Exception):
    """تُستخدم لإنهاء التدفق التفاعلي عند إغلاق تيار الإدخال."""


def _text(ar: str, en: str, lang: str) -> str:
    if lang == "en":
        return en
    if lang == "bi":
        return f"{ar} / {en}"
    return ar


def _ask(prompt: str, default: Optional[str] = None) -> str:
    suffix = f" [{default}]" if default is not None else ""
    try:
        value = input(f"{prompt}{suffix}: ").strip()
    except EOFError as exc:
        raise InputClosed() from exc
    if value:
        return value
    return default or ""


def _safe_print(ar: str, en: str, lang: str) -> None:
    print(_text(ar, en, lang))


def _bootstrap_language() -> str:
    # الافتراضي هو العربية
    return "ar"


def _choose_language(current_lang: str) -> str:
    print()
    _safe_print("تغيير اللغة:", "Switch Language:", current_lang)
    for idx, (_, label) in enumerate(LANGUAGE_OPTIONS, 1):
        mark = " *" if LANGUAGE_OPTIONS[idx - 1][0] == current_lang else ""
        print(f"  {idx}. {label}{mark}")
    print("  (* الحالية / * current)")

    raw = _ask(_text("اختر", "Select", current_lang), default="")
    if raw.isdigit():
        idx = int(raw)
        if 1 <= idx <= len(LANGUAGE_OPTIONS):
            return LANGUAGE_OPTIONS[idx - 1][0]
    _safe_print("إدخال غير صالح، سيتم الإبقاء على الإعداد الحالي", "Invalid input, keeping current", current_lang)
    return current_lang


def _ensure_st(lang: str) -> bool:
    config = get_config()
    session = config.create_session_manager()
    if session.token.st:
        return True

    print()
    _safe_print("لم يتم العثور على رمز الجلسة Session Token (ST)", "Session Token (ST) not found", lang)
    st = _ask(_text("يرجى إدخال رمز الجلسة ST (اتركه فارغاً للخروج)", "Please input ST (empty to exit)", lang), default="")
    if not st:
        return False
    session.token.st = st
    session.save()
    _safe_print(
        "تم: تم حفظ رمز الجلسة ST في ~/.google-flow/token.json",
        "Done: ST saved to ~/.google-flow/token.json",
        lang,
    )
    return True


def _parse_model_catalog() -> Dict[str, List[str]]:
    families: Dict[str, List[str]] = {}
    suffixes = [x[0] for x in ASPECT_OPTIONS]
    suffixes.sort(key=len, reverse=True)

    for model_id in IMAGE_MODELS:
        matched = None
        for suffix in suffixes:
            tail = f"-{suffix}"
            if model_id.endswith(tail):
                matched = suffix
                family = model_id[: -len(tail)]
                families.setdefault(family, [])
                if suffix not in families[family]:
                    families[family].append(suffix)
                break
        if not matched:
            families.setdefault(model_id, [])

    suffix_order = [x[0] for x in ASPECT_OPTIONS]
    for family in families:
        families[family].sort(key=lambda s: suffix_order.index(s) if s in suffix_order else 999)
    return families


def _model_to_family_aspect(model_id: str) -> Tuple[str, str]:
    for suffix, _, _ in sorted(ASPECT_OPTIONS, key=lambda x: len(x[0]), reverse=True):
        tail = f"-{suffix}"
        if model_id.endswith(tail):
            return model_id[: -len(tail)], suffix
    return model_id, DEFAULT_ASPECT


def _build_model_id(family: str, aspect: str, families: Dict[str, List[str]]) -> str:
    if family in families and aspect in families[family]:
        model_id = f"{family}-{aspect}"
        if model_id in IMAGE_MODELS:
            return model_id

    if family in families and families[family]:
        fallback_aspect = families[family][0]
        model_id = f"{family}-{fallback_aspect}"
        if model_id in IMAGE_MODELS:
            return model_id
    return DEFAULT_MODEL


def _choose_family(current: str, families: Dict[str, List[str]], lang: str) -> str:
    family_list = list(families.keys())
    print()
    _safe_print("عائلات النماذج المتاحة:", "Model families:", lang)
    for idx, fam in enumerate(family_list, 1):
        mark = " *" if fam == current else ""
        print(f"  {idx:2d}. {fam}{mark}")
    print("  (* الحالية / * current)")

    raw = _ask(_text("اختر", "Select", lang), default="")
    if raw.isdigit():
        idx = int(raw)
        if 1 <= idx <= len(family_list):
            return family_list[idx - 1]
    _safe_print("إدخال غير صالح، سيتم الإبقاء على الإعداد الحالي", "Invalid input, keeping current", lang)
    return current


def _choose_aspect(current: str, family: str, families: Dict[str, List[str]], lang: str) -> str:
    allowed = families.get(family, [])
    if not allowed:
        _safe_print(
            "لا توجد أبعاد متاحة لعائلة النموذج الحالية",
            "No available aspect for current family",
            lang,
        )
        return current

    print()
    _safe_print(f"الأبعاد المتاحة (العائلة: {family}):", f"Available aspects:", lang)
    entries = [x for x in ASPECT_OPTIONS if x[0] in allowed]
    for idx, (key, ar_desc, en_desc) in enumerate(entries, 1):
        mark = " *" if key == current else ""
        print(f"  {idx}. {ar_desc} ({key}){mark}")
    print("  (* الحالية / * current)")

    raw = _ask(_text("اختر (الافتراضي 1 - 16:9)", "Select (default 1 16:9)", lang), default="1")
    if raw.isdigit():
        idx = int(raw)
        if 1 <= idx <= len(entries):
            return entries[idx - 1][0]
    _safe_print("إدخال غير صالح، سيتم الإبقاء على الإعداد الحالي", "Invalid input, keeping current", lang)
    return current


def _choose_resolution(current: str, lang: str) -> str:
    print()
    _safe_print("خيارات الدقة المتاحة:", "Resolution options:", lang)
    for idx, (key, ar_desc, en_desc) in enumerate(RESOLUTION_OPTIONS, 1):
        mark = " *" if key == current else ""
        print(f"  {idx}. {ar_desc} ({key}){mark}")
    print("  (* الحالية / * current)")

    raw = _ask(_text("اختر (الافتراضي 1 - الصورة الأصلية)", "Select (default 1 Original)", lang), default="1")
    if raw.isdigit():
        idx = int(raw)
        if 1 <= idx <= len(RESOLUTION_OPTIONS):
            return RESOLUTION_OPTIONS[idx - 1][0]
    _safe_print("إدخال غير صالح، سيتم الإبقاء على الإعداد الحالي", "Invalid input, keeping current", lang)
    return current


def _load_reference_bytes(reference_path: str, lang: str) -> Optional[bytes]:
    if not reference_path:
        return None
    ref = Path(reference_path)
    if not ref.exists() or not ref.is_file():
        print(
            _text(
                f"تنبيه: الصورة المرجعية غير موجودة، تم التخطي: {reference_path}",
                f"Tip: reference image not found, ignored: {reference_path}",
                lang,
            )
        )
        return None
    return ref.read_bytes()


def _resolve_output_path(path: str) -> str:
    """توسيع قالب مسار الإخراج، يدعم {timestamp}."""
    if not path:
        return path
    return path.replace("{timestamp}", str(int(time.time())))


async def _generate_once(
    family: str,
    aspect: str,
    upscale: str,
    default_output: str,
    reference_path: str,
    families: Dict[str, List[str]],
    lang: str,
) -> None:
    model_id = _build_model_id(family, aspect, families)
    print()
    prompt = _ask(_text("يرجى إدخال الوصف (Prompt)", "Enter prompt", lang), default="")
    if not prompt:
        _safe_print("تنبيه: الوصف فارغ، تم الإلغاء", "Tip: empty prompt, cancelled", lang)
        return

    output_path = _ask(
        _text("مسار الإخراج (اتركه فارغاً للافتراضي)", "Output path (empty for default)", lang),
        default=default_output,
    )
    output_path = _resolve_output_path(output_path)
    reference_image = _load_reference_bytes(reference_path, lang)

    generator = ImageGenerator()
    result = await generator.generate(
        prompt=prompt,
        model=model_id,
        reference_image=reference_image,
        output_path=output_path or None,
        upscale=upscale,
    )

    if result.startswith("http"):
        print()
        print(_text(f"تم: رابط الصورة: {result}", f"Done: image URL: {result}", lang))
    else:
        print()
        print(_text(f"تم: مسار الحفظ: {result}", f"Done: saved path: {result}", lang))


def main() -> int:
    if sys.platform.startswith("win"):
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
            sys.stderr.reconfigure(encoding="utf-8", errors="replace")
        except AttributeError:
            pass
    try:
        lang = _bootstrap_language()
    except InputClosed:
        print("انتهى الإدخال، تم الخروج / Input closed, exited")
        return 0

    _safe_print("Flow توليد الصور التفاعلي", "Flow Interactive Image Generation", lang)
    print("-" * 40)

    try:
        if not _ensure_st(lang):
            _safe_print("لم يتم توفير رمز الجلسة ST، جاري الخروج", "No ST provided, exiting", lang)
            return 1
    except InputClosed:
        _safe_print("انتهى الإدخال، تم الخروج", "Input closed, exited", lang)
        return 0

    families = _parse_model_catalog()
    default_family, default_aspect = _model_to_family_aspect(DEFAULT_MODEL)

    family = default_family if default_family in families else list(families.keys())[0]
    aspect = DEFAULT_ASPECT
    if aspect not in families.get(family, []):
        aspect = families[family][0] if families.get(family) else DEFAULT_ASPECT
    upscale = DEFAULT_RESOLUTION
    reference_path = ""
    default_output = DEFAULT_OUTPUT_TEMPLATE

    while True:
        current_model = _build_model_id(family, aspect, families)

        # 获取当前分辨率和画幅的显示
        current_res_ar = next((x[1] for x in RESOLUTION_OPTIONS if x[0] == upscale), upscale)
        current_res_en = next((x[2] for x in RESOLUTION_OPTIONS if x[0] == upscale), upscale)
        current_aspect_ar = next((x[1] for x in ASPECT_OPTIONS if x[0] == aspect), aspect)
        current_aspect_en = next((x[2] for x in ASPECT_OPTIONS if x[0] == aspect), aspect)

        print()
        _safe_print("الإعدادات الحالية:", "Current:", lang)
        print(_text(f"  أبعاد الصورة: {current_aspect_ar}", f"  Aspect: {current_aspect_en}", lang))
        print(_text(f"  الدقة: {current_res_ar}", f"  Resolution: {current_res_en}", lang))
        print(_text(f"  النموذج: {current_model}", f"  Model: {current_model}", lang))
        print(_text(f"  الصورة المرجعية: {reference_path or 'لا يوجد'}", f"  Reference: {reference_path or 'None'}", lang))
        print(_text(f"  الإخراج: {default_output}", f"  Output: {default_output}", lang))

        print()
        _safe_print("القائمة:", "Menu:", lang)
        print(_text("  1) ضبط أبعاد الصورة", "  1) Configure Aspect Ratio", lang))
        print(_text("  2) ضبط الدقة", "  2) Configure Resolution", lang))
        print(_text("  3) بدء التوليد", "  3) Start Generation", lang))
        print(_text("  4) ضبط عائلة النموذج", "  4) Configure Model Family", lang))
        print(_text("  5) ضبط الصورة المرجعية", "  5) Configure Reference Image", lang))
        print(_text("  6) ضبط مسار الإخراج الافتراضي", "  6) Configure Default Output Path", lang))
        print(_text("  7) عرض النماذج المتاحة", "  7) View Available Models", lang))
        print(_text("  8) تغيير اللغة", "  8) Change Language", lang))
        print(_text("  0) خروج", "  0) Exit", lang))

        try:
            choice = _ask(_text("يرجى الاختيار", "Select", lang), default="3")
        except InputClosed:
            _safe_print("انتهى الإدخال، تم الخروج", "Input closed, exited", lang)
            return 0

        if choice == "1":
            aspect = _choose_aspect(aspect, family, families, lang)
        elif choice == "2":
            upscale = _choose_resolution(upscale, lang)
        elif choice == "3":
            try:
                asyncio.run(
                    _generate_once(
                        family=family,
                        aspect=aspect,
                        upscale=upscale,
                        default_output=default_output,
                        reference_path=reference_path,
                        families=families,
                        lang=lang,
                    )
                )
            except KeyboardInterrupt:
                print()
                _safe_print("تم مقاطعة المهمة الحالية", "Current task interrupted", lang)
            except InputClosed:
                print()
                _safe_print("انتهى الإدخال، تم الخروج", "Input closed, exited", lang)
                return 0
            except Exception as e:
                print()
                print(_text(f"خطأ: فشل التوليد: {e}", f"Error: generation failed: {e}", lang))
        elif choice == "4":
            family = _choose_family(family, families, lang)
            if aspect not in families.get(family, []):
                aspect = families[family][0]
        elif choice == "5":
            try:
                reference_path = _ask(
                    _text("أدخل مسار الصورة المرجعية (اتركه فارغاً للمسح)", "Input reference image path (empty to clear)", lang),
                    default=reference_path,
                )
            except InputClosed:
                _safe_print("انتهى الإدخال، تم الخروج", "Input closed, exited", lang)
                return 0
        elif choice == "6":
            try:
                default_output = _ask(
                    _text("أدخل مسار الإخراج الافتراضي", "Input default output path", lang),
                    default=default_output,
                )
            except InputClosed:
                _safe_print("انتهى الإدخال، تم الخروج", "Input closed, exited", lang)
                return 0
        elif choice == "7":
            print()
            _safe_print("قائمة النماذج المتاحة:", "Available models:", lang)
            for model_id, conf in IMAGE_MODELS.items():
                print(f"  - {model_id} :: {conf.get('description', '')}")
        elif choice == "8":
            lang = _choose_language(lang)
            _safe_print("تم: تحديث اللغة بنجاح", "Done: language updated", lang)
        elif choice == "0":
            _safe_print("تم الخروج", "Exited", lang)
            return 0
        else:
            _safe_print("تنبيه: خيار غير صالح", "Tip: invalid option", lang)


if __name__ == "__main__":
    raise SystemExit(main())
