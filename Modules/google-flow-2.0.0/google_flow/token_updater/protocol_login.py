"""curl_cffi fingerprint request protocol login labs.google — follow the NextAuth + Google OAuth process"""
import json
import re
from typing import Any

from curl_cffi.requests import AsyncSession

from .config import config
from .logger import logger
from google_flow.utils.proxy import parse_proxy

# Cookie name required by Google OAuth
_GOOGLE_COOKIE_NAMES = ("SID", "HSID", "SSID", "APISID", "SAPISID")


def _parse_google_cookies(raw: str) -> dict[str, str]:
    """Parse Google cookies input, supports JSON and plain text formats"""
    text = (raw or "").strip()
    if not text:
        return {}

    # Try JSON
    try:
        data = json.loads(text)
        if isinstance(data, list):
            result = {}
            for item in data:
                if isinstance(item, dict):
                    name = item.get("name", "")
                    value = item.get("value", "")
                    if name and value:
                        result[name] = value
            return result
        if isinstance(data, dict):
            cookies_list = data.get("cookies")
            if isinstance(cookies_list, list):
                result = {}
                for item in cookies_list:
                    if isinstance(item, dict):
                        name = item.get("name", "")
                        value = item.get("value", "")
                        if name and value:
                            result[name] = value
                return result
            return {k: v for k, v in data.items() if isinstance(v, str) and v}
    except (json.JSONDecodeError, ValueError):
        pass

    # Plain text format: name=value; name2=value2
    result = {}
    for part in text.split(";"):
        part = part.strip()
        if "=" in part:
            name, _, value = part.partition("=")
            name = name.strip()
            value = value.strip()
            if name and value:
                result[name] = value
    return result


def _build_cookie_header(cookies: dict[str, str]) -> str:
    return "; ".join(f"{k}={v}" for k, v in cookies.items())


def _get_set_cookies(headers) -> list[str]:
    """Safely obtain all Set-Cookie header values"""
    # curl_cffi Headers supports getlist()
    if hasattr(headers, "getlist"):
        return headers.getlist("set-cookie") or []
    if hasattr(headers, "get_list"):
        return headers.get_list("set-cookie") or []
    val = headers.get("set-cookie")
    return [val] if val else []


def _merge_cookies(cookies: dict[str, str], headers) -> None:
    """Merge cookies from response Set-Cookie header"""
    for val in _get_set_cookies(headers):
        parts = val.split(";")[0]
        if "=" in parts:
            name, _, value = parts.partition("=")
            cookies[name.strip()] = value.strip()


def _extract_session_token(headers) -> str | None:
    """Extract session token from Set-Cookie"""
    cookie_name = config.session_cookie_name
    for val in _get_set_cookies(headers):
        if val.startswith(f"{cookie_name}="):
            return val.split("=", 1)[1].split(";")[0].strip()
    return None


def _extract_redirect_from_html(text: str) -> str | None:
    """Extract jump URL from HTML response (meta refresh / JS location / form action)"""
    # <meta http-equiv="refresh" content="0;url=...">
    m = re.search(r'content\s*=\s*["\']?\d+\s*;\s*url\s*=\s*([^"\'>\s]+)', text, re.IGNORECASE)
    if m:
        return m.group(1)
    # window.location = "..." / location.href = "..." / location.replace("...")
    m = re.search(r'location\.(?:href|replace)\s*\(\s*["\']([^"\']+)["\']', text, re.IGNORECASE)
    if m:
        return m.group(1)
    m = re.search(r'location\s*=\s*["\']([^"\']+)["\']', text, re.IGNORECASE)
    if m:
        return m.group(1)
    # <form action="..."> Automatic submission
    m = re.search(r'<form[^>]*action\s*=\s*["\']([^"\']+)["\']', text, re.IGNORECASE)
    if m:
        return m.group(1)
    # URL parameters in accounts.google.com page
    m = re.search(r'(https://labs\.google/fx/api/auth/callback/google[^"\'<>\s]*)', text)
    if m:
        return m.group(1)
    # continue parameter
    m = re.search(r'[&?]continue=([^"\'<>\s&]+)', text)
    if m:
        from urllib.parse import unquote
        return unquote(m.group(1))
    return None


class ProtocolLogin:
    """curl_cffi fingerprint request protocol login labs.google"""

    LABS_BASE = "https://labs.google/fx"
    IMPERSONATE = "chrome124"

    def _get_proxy_url(self, proxy_str: str | None) -> str | None:
        if not proxy_str:
            return None
        proxy_config = parse_proxy(proxy_str)
        if not proxy_config:
            return None
        server = proxy_config.get("server", "")
        username = proxy_config.get("username", "")
        password = proxy_config.get("password", "")
        if not server:
            return None
        if username and password:
            # Inject authentication information into the URL
            scheme, _, rest = server.partition("://")
            return f"{scheme}://{username}:{password}@{rest}"
        return server

    async def login(
        self,
        google_cookies_raw: str,
        proxy: str | None = None,
        email: str | None = None,
    ) -> dict[str, Any]:
        """
        Agreement login.

        Input: Google cookies (JSON or plain text, requires SID/HSID/SSID/APISID/SAPISID)
        Output: {"success": bool, "session_token": str, "error": str}
        """
        google_cookies = _parse_google_cookies(google_cookies_raw)
        has_required = any(name in google_cookies for name in _GOOGLE_COOKIE_NAMES)
        if not has_required:
            return {
                "success": False,
                "error": "No valid Google cookie found (requires at least one of SID/HSID/SSID/APISID/SAPISID)",
            }

        proxy_url = self._get_proxy_url(proxy)
        session_kwargs = {"impersonate": self.IMPERSONATE}
        if proxy_url:
            session_kwargs["proxy"] = proxy_url

        async with AsyncSession(**session_kwargs) as s:
            try:
                # Step 1: Get CSRF token
                logger.info("[Protocol Login] Get CSRF token...")
                resp = await s.get(f"{self.LABS_BASE}/api/auth/csrf")
                if resp.status_code != 200:
                    return {"success": False, "error": f"CSRF failed: HTTP {resp.status_code}"}

                csrf_token = resp.json().get("csrfToken")
                if not csrf_token:
                    return {"success": False, "error": "No csrfToken in CSRF response"}

                labs_cookies = {}
                _merge_cookies(labs_cookies, resp.headers)

                # Step 2: POST signin/google → Get OAuth redirect URL
                logger.info("[Protocol Login] Request Google OAuth URL...")
                resp = await s.post(
                    f"{self.LABS_BASE}/api/auth/signin/google",
                    data={
                        "csrfToken": csrf_token,
                        "callbackUrl": "https://labs.google/fx",
                        "json": "true",
                    },
                    headers={
                        "Referer": self.LABS_BASE,
                        "Origin": "https://labs.google",
                        "Cookie": _build_cookie_header(labs_cookies) if labs_cookies else "",
                    },
                    allow_redirects=False,
                )
                if resp.status_code != 200:
                    return {"success": False, "error": f"Signin failed: HTTP {resp.status_code}"}

                _merge_cookies(labs_cookies, resp.headers)
                signin_data = resp.json()
                redirect_url = signin_data.get("redirect") or signin_data.get("url")
                if not redirect_url:
                    return {"success": False, "error": f"No redirect URL: {json.dumps(signin_data)[:200]}"}

                # Added login_hint to skip account selector
                if email:
                    from urllib.parse import parse_qs, urlencode, urlparse
                    parsed = urlparse(redirect_url)
                    qs = parse_qs(parsed.query)
                    qs["login_hint"] = [email]
                    new_query = urlencode({k: v[0] for k, v in qs.items()}, doseq=True)
                    redirect_url = f"{parsed.scheme}://{parsed.netloc}{parsed.path}?{new_query}"
                    logger.info(f"[Protocol login] Add login_hint={email}")

                from urllib.parse import urljoin

                # Step 3: Follow the OAuth redirect chain with Google cookies
                logger.info("[Protocol Login] Follow Google OAuth redirects...")
                google_cookie_header = _build_cookie_header(google_cookies)
                callback_url = None
                current_url = redirect_url

                for i in range(10):
                    resp = await s.get(
                        current_url,
                        headers={
                            "Cookie": google_cookie_header,
                            "Referer": "https://labs.google/" if i == 0 else "https://accounts.google.com/",
                        },
                        allow_redirects=False,
                    )
                    location = resp.headers.get("location")

                    # Check if there is a callback URL
                    check_url = location or ""
                    if "labs.google/fx/api/auth/callback/google" in check_url:
                        callback_url = check_url
                        break

                    if location:
                        logger.info(f"[Protocol Login] Redirect to: {location[:100]}...")
                        current_url = location
                        continue

                    # Without Location header, try to extract jump from HTML
                    if resp.status_code == 200:
                        body = resp.text or ""

                        # Check if rejected
                        if "/v3/signin/rejected" in body or "signin/rejected" in body:
                            return {"success": False, "error": "Google refuses to log in. Cookies may have expired or been risk controlled. Please export again."}

                        html_redirect = _extract_redirect_from_html(body)
                        if html_redirect:
                            # Relative path completion to absolute URL
                            if html_redirect.startswith("/"):
                                html_redirect = urljoin(current_url, html_redirect)
                            logger.info(f"[Protocol Login] Extract from HTML to jump: {html_redirect[:100]}...")
                            if "labs.google/fx/api/auth/callback/google" in html_redirect:
                                callback_url = html_redirect
                                break
                            current_url = html_redirect
                            continue

                    return {"success": False, "error": f"Google OAuth did not return redirect (HTTP {resp.status_code})"}

                if not callback_url:
                    return {"success": False, "error": "Not getting callback URL in Google OAuth flow"}

                # Step 4: Access callback in exchange for session cookie
                logger.info("[Protocol login] Exchange auth code for session...")
                resp = await s.get(
                    callback_url,
                    headers={
                        "Cookie": _build_cookie_header(labs_cookies),
                        "Referer": "https://accounts.google.com/",
                    },
                    allow_redirects=False,
                )

                session_token = _extract_session_token(resp.headers)

                # callback may redirect multiple times, follow until you get the session token
                for _ in range(5):
                    if session_token:
                        break
                    location = resp.headers.get("location")
                    if not location or resp.status_code not in (301, 302, 303, 307, 308):
                        break
                    _merge_cookies(labs_cookies, resp.headers)
                    resp = await s.get(
                        location,
                        headers={"Cookie": _build_cookie_header(labs_cookies)},
                        allow_redirects=False,
                    )
                    session_token = _extract_session_token(resp.headers)

                if not session_token:
                    return {"success": False, "error": "The session token was not obtained. The Google session may have expired."}

                logger.info("[Protocol Login] Login successful")
                return {"success": True, "session_token": session_token}

            except Exception as e:
                logger.error(f"[Protocol Login] Exception: {e}")
                return {"success": False, "error": str(e)}


protocol_loginer = ProtocolLogin()
