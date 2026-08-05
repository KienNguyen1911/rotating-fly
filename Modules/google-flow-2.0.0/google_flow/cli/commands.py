"""
Flow Image CLI — command-line interface.

Provides ``generate``, ``models``, ``credits``, ``login``, and
``config`` commands with rich formatted output.
"""

from __future__ import annotations

import argparse
import asyncio
import sys
import time
from pathlib import Path
from typing import TYPE_CHECKING

from google_flow._version import __version__
from google_flow.config import get_config
from google_flow.constants import DEFAULT_MODEL_ID
from google_flow.core.client import FlowClient
from google_flow.core.generator import ImageGenerator
from google_flow.logging import get_logger, setup_logging
from google_flow.models.registry import print_models

if TYPE_CHECKING:
    from google_flow.types import CreditsInfo

logger = get_logger(__name__)


def _create_generator() -> ImageGenerator:
    """Build a fully-configured ImageGenerator."""
    config = get_config()
    session = config.create_session_manager()
    client = FlowClient(
        labs_base_url=config.flow.labs_base_url,
        api_base_url=config.flow.api_base_url,
        timeout=config.flow.timeout,
    )
    captcha = config.create_captcha_provider(session.token.st)
    return ImageGenerator(
        client=client,
        session=session,
        captcha_provider=captcha.get_token,
        max_retries=config.flow.max_retries,
    )


def main() -> int:
    """CLI entry point."""
    if sys.platform.startswith("win"):
        try:
            reconfig_out = getattr(sys.stdout, "reconfigure", None)
            if reconfig_out:
                reconfig_out(encoding="utf-8", errors="replace")
            reconfig_err = getattr(sys.stderr, "reconfigure", None)
            if reconfig_err:
                reconfig_err(encoding="utf-8", errors="replace")
        except Exception:
            pass

    parser = argparse.ArgumentParser(
        prog="google-flow",
        description="أداة سطر الأوامر لتوليد صور Flow",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""\
أمثلة:
  # توليد صورة من نص
  google-flow generate "قطة لطيفة تلعب في الحديقة"

  # تحديد النموذج ومسار الإخراج
  google-flow gen "لوحة مناظر طبيعية" -m gemini-3.0-pro-image-landscape -o landscape.png

  # توليد صورة من صورة
  google-flow gen "حول هذه الصورة إلى نمط ألوان مائية" -r input.jpg -o output.png

  # عرض الرصيد
  google-flow credits

  # تسجيل الدخول
  google-flow login --st "your-session-token"
""",
    )

    parser.add_argument(
        "-V", "--version", action="version", version=f"%(prog)s {__version__}"
    )
    parser.add_argument(
        "-d", "--debug", action="store_true", help="تفعيل وضع تصحيح الأخطاء"
    )

    subparsers = parser.add_subparsers(dest="command", help="الأوامر المتاحة")

    # Generate
    gen_parser = subparsers.add_parser(
        "generate", aliases=["gen", "g"], help="توليد صورة"
    )
    gen_parser.add_argument("prompt", help="الوصف النصي لتوليد الصورة")
    gen_parser.add_argument(
        "-m", "--model", default=DEFAULT_MODEL_ID, help="اسم النموذج"
    )
    gen_parser.add_argument("-o", "--output", help="مسار ملف الإخراج")
    gen_parser.add_argument(
        "-r", "--reference", help="مسار الصورة المرجعية"
    )
    gen_parser.add_argument(
        "-u",
        "--upscale",
        choices=["none", "2k", "4k"],
        default="none",
        help="تكبير الدقة",
    )

    # Models
    subparsers.add_parser("models", aliases=["m"], help="عرض النماذج المتاحة")

    # Credits
    subparsers.add_parser("credits", aliases=["c"], help="الاستعلام عن الرصيد")

    # Login
    login_parser = subparsers.add_parser(
        "login", aliases=["l"], help="تسجيل الدخول"
    )
    login_parser.add_argument("--st", required=True, help="رمز الجلسة")

    # Config
    subparsers.add_parser("config", help="عرض الإعدادات الحالية")

    args = parser.parse_args()

    if args.debug:
        import logging

        setup_logging(level=logging.DEBUG)

    if args.command in ("generate", "gen", "g"):
        return cmd_generate(args)
    elif args.command in ("models", "m"):
        return cmd_models()
    elif args.command in ("credits", "c"):
        return cmd_credits()
    elif args.command in ("login", "l"):
        return cmd_login(args.st)
    elif args.command == "config":
        return cmd_config()
    else:
        parser.print_help()
        return 0


# ── Commands ────────────────────────────────────────────────────────

def cmd_generate(args: argparse.Namespace) -> int:
    """Generate an image."""
    config = get_config()
    session = config.create_session_manager()

    if not session.token.has_session:
        print("❌ لم يتم تسجيل الدخول. شغّل:")
        print("   google-flow login --st <session-token>")
        return 1

    try:
        generator = _create_generator()

        reference_image = None
        if args.reference:
            ref_path = Path(args.reference)
            if not ref_path.exists():
                print(f"❌ الصورة المرجعية غير موجودة: {args.reference}")
                return 1
            reference_image = ref_path.read_bytes()

        output_path = args.output
        if not output_path:
            timestamp = int(time.time())
            output_path = f"output/flow_{timestamp}.png"

        result = asyncio.run(
            _run_generate(
                generator,
                args.prompt,
                args.model,
                reference_image,
                output_path,
                args.upscale,
            )
        )

        print("\n✅ تم!")
        if result.startswith("http"):
            print(f"   🔗 رابط الصورة: {result}")
        else:
            print(f"   📁 مسار الحفظ: {result}")

        return 0

    except Exception as e:
        print(f"❌ فشل التوليد: {e}")
        return 1


async def _run_generate(
    generator: ImageGenerator,
    prompt: str,
    model: str,
    reference_image: bytes | None,
    output_path: str,
    upscale: str,
) -> str:
    async with generator.client:
        return await generator.generate(
            prompt,
            model=model,
            reference_image=reference_image,
            output_path=output_path,
            upscale=upscale,
        )


def cmd_models() -> int:
    """List available models."""
    print_models()
    return 0


def cmd_credits() -> int:
    """Check account credits."""
    config = get_config()
    session = config.create_session_manager()

    if not session.token.has_session:
        print("❌ لم يتم تسجيل الدخول. شغّل:")
        print("   google-flow login --st <session-token>")
        return 1

    try:
        generator = _create_generator()
        credits_info = asyncio.run(_run_credits(generator))

        print("\n📊 معلومات الحساب")
        print(f"   💰 Credits: {credits_info.credits}")
        print(f"   🏷️  المستوى: {credits_info.tier}")
        return 0

    except Exception as e:
        print(f"❌ فشل الاستعلام: {e}")
        return 1


async def _run_credits(generator: ImageGenerator) -> CreditsInfo:
    async with generator.client:
        return await generator.check_credits()


def cmd_login(st: str) -> int:
    """Login with session token."""
    config = get_config()
    session = config.create_session_manager()
    session.token.st = st
    session.save()

    print("✅ تم حفظ رمز الجلسة")
    print("\n🔄 جاري التحقق...")

    try:
        generator = _create_generator()
        credits_info = asyncio.run(_run_credits(generator))

        print("✅ تم تسجيل الدخول بنجاح!")
        print(f"   💰 Credits: {credits_info.credits}")
        print(f"   🏷️  المستوى: {credits_info.tier}")
        return 0

    except Exception as e:
        print(f"⚠️  فشل التحقق: {e}")
        print("   تم حفظ الرمز، ولكن قد يكون غير صالح")
        return 1


def cmd_config() -> int:
    """Show current configuration."""
    config = get_config()
    session = config.create_session_manager()

    print("\n⚙️  الإعدادات الحالية")
    print("─" * 40)
    print(f"  Flow API:    {config.flow.api_base_url}")
    print(f"  مجلد الإخراج: {config.output_dir}")
    print(f"  Debug:       {config.debug}")
    print(f"  Captcha:     {config.captcha.method}")
    print("─" * 40)

    if session.token.has_session:
        print(f"  ST: {session.token.st[:20]}...")
    else:
        print("  ST: غير مهيأ")

    if session.token.has_access_token:
        print(f"  AT: {session.token.at[:20]}...")
    else:
        print("  AT: لم يتم الحصول عليه")

    if session.token.has_project:
        print(f"  Project: {session.token.project_id[:20]}...")
    else:
        print("  Project: لم يتم إنشاؤه")

    print("─" * 40)
    return 0


if __name__ == "__main__":
    sys.exit(main())
