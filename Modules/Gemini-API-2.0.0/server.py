import asyncio
import json
import logging
import os
import sys
import uuid
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Dict, List, Optional

sys.path.insert(0, str(Path(__file__).resolve().parent / "src"))

from fastapi import FastAPI, HTTPException, Query, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field

from gemini_webapi import GeminiClient, ChatSession
from gemini_webapi.constants import AccountStatus
from gemini_webapi.exceptions import GeminiError
from gemini_webapi.utils.load_browser_cookies import load_browser_cookies, HAS_BC3

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------
log = logging.getLogger("server")
logging.basicConfig(level=logging.INFO, format="%(asctime)s | %(levelname)s | %(name)s | %(message)s")

# ---------------------------------------------------------------------------
# Global State
# ---------------------------------------------------------------------------
client: Optional[GeminiClient] = None
chat_sessions: Dict[str, ChatSession] = {}
_init_lock = asyncio.Lock()
_cookies_mtime: float = 0.0  # Last known mtime of cookies.json
_watcher_task: Optional[asyncio.Task] = None

REINIT_COOLDOWN = 60  # seconds — minimum interval between re-init attempts
_last_init_time: float = 0.0

# ---------------------------------------------------------------------------
# Cookie Helpers
# ---------------------------------------------------------------------------

def get_cookies_path() -> Path:
    env_path = os.getenv("GEMINI_COOKIE_PATH")
    if env_path:
        return Path(env_path) / "cookies.json"
    return Path(__file__).resolve().parent / "cookies.json"


def load_cookies_from_file() -> dict:
    """
    Read and parse cookies.json, returning the cookies dict.
    Raises RuntimeError if the file is missing or cookies are invalid.
    """
    cookie_file = get_cookies_path()
    if not cookie_file.exists():
        raise RuntimeError(f"Cookie file not found at {cookie_file}. Please ensure cookies.json exists.")

    data = json.loads(cookie_file.read_text(encoding="utf-8"))
    cookies = data.get("cookies", data)

    if not cookies.get("__Secure-1PSID"):
        raise RuntimeError("Missing __Secure-1PSID in cookies file.")

    return cookies


def load_cookies_from_browser() -> dict | None:
    """
    Try to load cookies from installed browsers (Chrome, Edge, etc.).
    Returns a dict of cookie name->value if successful, None otherwise.
    
    Note: This may fail if the browser is currently running and has locked
    its cookie database. Works best when the browser is closed.
    """
    if not HAS_BC3:
        log.warning("browser_cookie3 is not installed. Cannot load browser cookies.")
        return None

    try:
        browser_cookies = load_browser_cookies(domain_name="google.com", verbose=True)
        if not browser_cookies:
            log.info("No browser cookies found for google.com")
            return None

        # Pick the first browser that has __Secure-1PSID
        for browser_name, cookie_list in browser_cookies.items():
            cookies_dict = {c["name"]: c["value"] for c in cookie_list}
            if cookies_dict.get("__Secure-1PSID"):
                log.info(f"Loaded cookies from browser: {browser_name} ({len(cookie_list)} cookies)")
                return cookies_dict

        log.info("Browser cookies found but none contain __Secure-1PSID")
        return None

    except Exception as e:
        log.warning(f"Failed to load browser cookies: {e}")
        return None


def clear_temp_cookie_cache():
    """Clear stale .cached_cookies_*.json files in temp directory to prevent loading expired tokens."""
    try:
        import tempfile
        cache_dir = Path(tempfile.gettempdir()) / "gemini_webapi"
        if cache_dir.exists():
            for f in cache_dir.glob("*.json"):
                try:
                    f.unlink()
                except Exception:
                    pass
            log.info("Cleared temp cookie cache directory.")
    except Exception as e:
        log.warning(f"Could not clear temp cookie cache: {e}")


async def init_client(force: bool = False, cookies_override: dict | None = None) -> bool:
    """
    Initialize (or re-initialize) the global GeminiClient.
    
    If cookies_override is provided, uses those cookies directly.
    Otherwise reads from cookies.json.
    
    Returns True if a new client was successfully initialized, False if skipped.
    Uses a lock to prevent concurrent init attempts and a cooldown timer.
    """
    import time

    global client, _last_init_time, _cookies_mtime

    # Cooldown check (outside lock for fast reject)
    if not force and (time.time() - _last_init_time) < REINIT_COOLDOWN:
        return False

    async with _init_lock:
        # Double-check cooldown inside lock
        if not force and (time.time() - _last_init_time) < REINIT_COOLDOWN:
            return False

        try:
            cookies = cookies_override if cookies_override else load_cookies_from_file()

            # Close existing client gracefully
            if client is not None:
                try:
                    await client.close()
                except Exception:
                    pass

            # Clear any old cached cookie files from tempdir
            clear_temp_cookie_cache()

            # Create new client with all cookies from the start
            psid = cookies.get("__Secure-1PSID")
            psidts = cookies.get("__Secure-1PSIDTS")

            new_client = GeminiClient(psid, psidts, verify=False)
            new_client.cookies = cookies  # Set all 23+ cookies
            await new_client.init(auto_refresh=True)

            client = new_client
            _last_init_time = time.time()

            # Update known mtime
            cookie_file = get_cookies_path()
            if cookie_file.exists():
                _cookies_mtime = cookie_file.stat().st_mtime

            # Log the status
            status_name = client.account_status.name if client else "UNKNOWN"
            if client and client.account_status == AccountStatus.AVAILABLE:
                log.info(f"✅ Gemini client initialized — Account status: {status_name}")
            else:
                log.warning(f"⚠️  Gemini client initialized — Account status: {status_name}")

            return True

        except Exception as e:
            log.error(f"❌ Failed to initialize Gemini client: {e}")
            _last_init_time = time.time()  # Respect cooldown even on failure
            raise


async def try_reinit_if_needed() -> bool:
    """
    Check if the client needs re-initialization and attempt it.
    Tries in order: 1) cookies.json (if changed), 2) browser cookies.
    Returns True if re-init was performed successfully.
    """
    if client is None:
        try:
            return await init_client(force=True)
        except Exception:
            return False

    if client.account_status != AccountStatus.AVAILABLE:
        # Strategy 1: Check if cookies.json has been updated
        cookie_file = get_cookies_path()
        if cookie_file.exists() and cookie_file.stat().st_mtime > _cookies_mtime:
            log.info("🔄 cookies.json changed, attempting re-initialization...")
            try:
                return await init_client(force=True)
            except Exception as e:
                log.warning(f"Re-init from cookies.json failed: {e}")

        # Strategy 2: Try loading fresh cookies from browser
        browser_cookies = load_cookies_from_browser()
        if browser_cookies:
            log.info("🌐 Trying re-init with browser cookies...")
            try:
                return await init_client(force=True, cookies_override=browser_cookies)
            except Exception as e:
                log.warning(f"Re-init from browser cookies failed: {e}")

    return False


# ---------------------------------------------------------------------------
# Background File Watcher
# ---------------------------------------------------------------------------

async def watch_cookies_file(interval: float = 5.0):
    """
    Background task that monitors cookies.json for changes.
    When a change is detected, automatically re-initializes the client.
    """
    global _cookies_mtime

    cookie_file = get_cookies_path()
    log.info(f"👀 Watching {cookie_file} for changes (every {interval}s)")

    while True:
        try:
            await asyncio.sleep(interval)

            if not cookie_file.exists():
                continue

            current_mtime = cookie_file.stat().st_mtime
            if current_mtime > _cookies_mtime:
                log.info(f"🔄 Detected cookies.json change (mtime: {current_mtime:.0f} > {_cookies_mtime:.0f})")
                _cookies_mtime = current_mtime  # Update immediately to avoid repeated triggers

                try:
                    await init_client(force=True)
                    log.info("✅ Auto re-initialized client from updated cookies.json")
                except Exception as e:
                    log.warning(f"Auto re-init failed: {e}")

        except asyncio.CancelledError:
            log.info("👀 Cookie file watcher stopped")
            break
        except Exception as e:
            log.warning(f"Cookie watcher error: {e}")
            await asyncio.sleep(interval)


# ---------------------------------------------------------------------------
# Application Lifecycle
# ---------------------------------------------------------------------------

@asynccontextmanager
async def lifespan(app: FastAPI):
    global _watcher_task, _cookies_mtime

    # Startup: Initialize Gemini Client
    cookie_file = get_cookies_path()
    if cookie_file.exists():
        _cookies_mtime = cookie_file.stat().st_mtime

    await init_client(force=True)

    # Start background cookie file watcher
    _watcher_task = asyncio.create_task(watch_cookies_file(interval=5.0))

    yield

    # Shutdown
    if _watcher_task:
        _watcher_task.cancel()
        try:
            await _watcher_task
        except asyncio.CancelledError:
            pass

    if client:
        await client.close()


app = FastAPI(
    title="Gemini WebAPI REST Server",
    description="REST API service providing access to Google Gemini Web, Gems, Deep Research, and Extensions.",
    version="2.0.0",
    lifespan=lifespan,
)

# Enable CORS for frontend integrations
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# ---------------------------------------------------------------------------
# Middleware: Auto re-init on UNAUTHENTICATED
# ---------------------------------------------------------------------------

@app.middleware("http")
async def auto_reinit_middleware(request: Request, call_next):
    """
    Before processing API requests, check if the client is UNAUTHENTICATED.
    If so, attempt to re-init from cookies.json (if it has been updated).
    """
    if request.url.path.startswith("/api/") and request.url.path != "/api/health":
        await try_reinit_if_needed()

    response = await call_next(request)
    return response


# ---------------------------------------------------------------------------
# Models & Schemas
# ---------------------------------------------------------------------------

class GemResponse(BaseModel):
    id: str
    name: str
    description: Optional[str] = None
    prompt: Optional[str] = None
    predefined: bool

class ChatRequest(BaseModel):
    message: str = Field(..., description="The message or prompt to send to Gemini.")
    gem_id: Optional[str] = Field(None, description="Optional Gem ID (e.g. 'coding-partner' or custom Gem ID) to apply system prompt.")
    session_id: Optional[str] = Field(None, description="Optional session ID to maintain continuous conversation history.")
    deep_research: bool = Field(False, description="Set to True to trigger automated Deep Research mode.")
    temporary: bool = Field(False, description="Set to True to prevent saving conversation in Gemini history.")

class ImageOutput(BaseModel):
    url: str
    title: Optional[str] = None
    alt: Optional[str] = None

class ChatResponse(BaseModel):
    session_id: str
    text: str
    thoughts: Optional[str] = None
    images: List[ImageOutput] = []
    deep_research_completed: bool = False

# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@app.get("/api/health", summary="Health Check")
async def health_check():
    """Kiểm tra trạng thái hoạt động của API Server và Gemini Client."""
    if client is None:
        return {
            "status": "error",
            "initialized": False,
            "account_status": None,
            "message": "Client not initialized",
        }

    status = client.account_status
    return {
        "status": "ok" if status == AccountStatus.AVAILABLE else "degraded",
        "initialized": True,
        "account_status": status.name,
        "account_description": status.description if hasattr(status, 'description') else str(status),
        "cookies_path": str(get_cookies_path()),
    }


@app.post("/api/refresh-cookies", summary="Reload Cookies & Re-init Client")
async def refresh_cookies():
    """
    Reload cookies từ file cookies.json và re-initialize Gemini Client.
    Hữu ích sau khi chạy convert-cookies.py để cập nhật cookies mới.
    """
    try:
        success = await init_client(force=True)
        if success and client:
            status = client.account_status
            return {
                "status": "ok",
                "account_status": status.name,
                "message": f"Client re-initialized successfully. Status: {status.name}",
            }
        return {"status": "error", "message": "Re-init was skipped or client is None"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to re-init client: {str(e)}")


@app.post("/api/refresh-from-browser", summary="Load Cookies from Browser & Re-init")
async def refresh_from_browser():
    """
    Tự động đọc cookies từ browser đã cài đặt (Chrome, Edge, etc.) và re-initialize client.
    Browser có thể cần được đóng để truy cập cookie database.
    """
    browser_cookies = load_cookies_from_browser()
    if not browser_cookies:
        raise HTTPException(
            status_code=404,
            detail="Could not load cookies from any browser. Make sure you are logged in to gemini.google.com in Chrome/Edge."
        )

    try:
        success = await init_client(force=True, cookies_override=browser_cookies)
        if success and client:
            status = client.account_status
            return {
                "status": "ok",
                "source": "browser",
                "account_status": status.name,
                "message": f"Client re-initialized from browser cookies. Status: {status.name}",
            }
        return {"status": "error", "message": "Re-init was skipped or client is None"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to re-init from browser: {str(e)}")


@app.get("/api/gems", response_model=List[GemResponse], summary="List Gems")
async def list_gems(include_hidden: bool = Query(False, description="Include hidden predefined system gems.")):
    """
    Lấy danh sách tất cả các Gem đang có trong tài khoản (bao gồm cả System Gems và Custom Gems).
    """
    if client is None:
        raise HTTPException(status_code=503, detail="Gemini client is not initialized.")
    try:
        gems_jar = await client.fetch_gems(include_hidden=include_hidden)
        result = []
        for gem in gems_jar:
            result.append(
                GemResponse(
                    id=gem.id,
                    name=gem.name,
                    description=gem.description,
                    prompt=gem.prompt,
                    predefined=gem.predefined,
                )
            )
        return result
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to fetch gems: {str(e)}")


@app.post("/api/chat", response_model=ChatResponse, summary="Send Message / Chat with Gem")
async def chat_with_gem(req: ChatRequest):
    """
    Gửi tin nhắn trò chuyện với Gemini. Hỗ trợ chọn Gem, Deep Research, và Gemini Extensions.
    """
    if client is None:
        raise HTTPException(status_code=503, detail="Gemini client is not initialized.")

    try:
        # Manage Session ID
        session_id = req.session_id or str(uuid.uuid4())
        
        if session_id not in chat_sessions:
            # Create new chat session with specified gem if provided
            chat_kwargs = {}
            if req.gem_id:
                chat_kwargs["gem"] = req.gem_id
            chat_sessions[session_id] = client.start_chat(**chat_kwargs)
            
        chat = chat_sessions[session_id]

        # Case 1: Automated Deep Research
        if req.deep_research:
            plan = await client.create_deep_research_plan(req.message, chat=chat)
            await client.start_deep_research(plan, chat=chat)
            research_result = await client.wait_for_deep_research(plan, poll_interval=10.0, timeout=600.0)
            
            result_text = research_result.text or (research_result.final_output.text if research_result.final_output else "")
            return ChatResponse(
                session_id=session_id,
                text=result_text,
                deep_research_completed=True,
            )

        # Case 2: Normal / Extended Chat (supports @YouTube, @Gmail extensions in req.message)
        output = await chat.send_message(req.message, temporary=req.temporary)
        
        images_data = []
        if output.images:
            for img in output.images:
                images_data.append(
                    ImageOutput(
                        url=getattr(img, "url", ""),
                        title=getattr(img, "title", None),
                        alt=getattr(img, "alt", None),
                    )
                )

        return ChatResponse(
            session_id=session_id,
            text=output.text or "",
            thoughts=output.thoughts,
            images=images_data,
            deep_research_completed=False,
        )

    except GeminiError as e:
        raise HTTPException(status_code=400, detail=f"Gemini API Error: {str(e)}")
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Internal Server Error: {str(e)}")


@app.post("/api/chat/stream", summary="Stream Chat Response (SSE)")
async def chat_stream(req: ChatRequest):
    """
    Gửi tin nhắn và nhận phản hồi theo thời gian thực (Server-Sent Events / SSE Stream).
    """
    if client is None:
        raise HTTPException(status_code=503, detail="Gemini client is not initialized.")

    session_id = req.session_id or str(uuid.uuid4())
    if session_id not in chat_sessions:
        chat_kwargs = {}
        if req.gem_id:
            chat_kwargs["gem"] = req.gem_id
        chat_sessions[session_id] = client.start_chat(**chat_kwargs)
        
    chat = chat_sessions[session_id]

    async def event_generator():
        try:
            yield f"data: {json.dumps({'event': 'start', 'session_id': session_id})}\n\n"
            async for chunk in chat.send_message_stream(req.message, temporary=req.temporary):
                if chunk.text_delta:
                    payload = json.dumps({"event": "delta", "delta": chunk.text_delta})
                    yield f"data: {payload}\n\n"
            yield f"data: {json.dumps({'event': 'done', 'session_id': session_id})}\n\n"
        except Exception as e:
            yield f"data: {json.dumps({'event': 'error', 'detail': str(e)})}\n\n"

    return StreamingResponse(event_generator(), media_type="text/event-stream")


@app.get("/api/sessions", summary="List Active Chat Sessions")
async def list_active_sessions():
    """Lấy danh sách tất cả các phiên chat đang hoạt động trên Server."""
    return {"active_sessions": list(chat_sessions.keys())}


@app.delete("/api/sessions/{session_id}", summary="Delete Active Chat Session")
async def delete_active_session(session_id: str):
    """Xóa một phiên chat đang lưu trữ trên Server."""
    if session_id in chat_sessions:
        del chat_sessions[session_id]
        return {"status": "ok", "message": f"Session {session_id} deleted."}
    raise HTTPException(status_code=404, detail=f"Session {session_id} not found.")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
