"""Proxy format parsing and formatting utilities."""
from urllib.parse import urlparse


def parse_proxy(proxy_str: str) -> dict | None:
    """
    Parse proxy strings, supporting multiple formats:

    HTTP/HTTPS:
    - http://host:port
    - http://user:pass@host:port
    - https://host:port

    SOCKS5:
    - socks5://host:port
    - socks5://user:pass@host:port
    - socks5h://host:port (DNS resolved through proxy)
    - socks5h://user:pass@host:port

    Abbreviated format (automatically recognized):
    - host:port (default http)
    - user:pass@host:port (default http)

    Returns:
        {"server": "protocol://host:port", "username": "...", "password": "..."}
        or None (invalid format)
    """
    if not proxy_str or not proxy_str.strip():
        return None

    proxy_str = proxy_str.strip()

    # If there is no protocol prefix, try smart identification
    if "://" not in proxy_str:
        # Check if there is authentication information
        proxy_str = f"http://{proxy_str}" if "@" in proxy_str else f"http://{proxy_str}"

    try:
        parsed = urlparse(proxy_str)

        # Verification protocol
        valid_schemes = ["http", "https", "socks5", "socks5h"]
        if parsed.scheme not in valid_schemes:
            return None

        # Verify host and port
        if not parsed.hostname or not parsed.port:
            return None

        result = {
            "server": f"{parsed.scheme}://{parsed.hostname}:{parsed.port}"
        }

        # Extract authentication information
        if parsed.username:
            result["username"] = parsed.username
        if parsed.password:
            result["password"] = parsed.password

        return result

    except Exception:
        return None


def format_proxy_for_playwright(proxy_config: dict) -> dict | None:
    """
    Convert parsed proxy configuration to Playwright format

    Playwright proxy format:
    {
        "server": "http://host:port" or "socks5://host:port",
        "username": "...", # optional
        "password": "..." # optional
    }
    """
    if not proxy_config:
        return None

    result = {"server": proxy_config["server"]}

    if "username" in proxy_config:
        result["username"] = proxy_config["username"]
    if "password" in proxy_config:
        result["password"] = proxy_config["password"]

    return result


def validate_proxy_format(proxy_str: str) -> tuple[bool, str]:
    """
    Validate proxy format

    Returns:
        (is_valid, message)
    """
    if not proxy_str or not proxy_str.strip():
        return True, "No proxy"

    result = parse_proxy(proxy_str)

    if result is None:
        return False, "Invalid proxy format"

    # Build description
    server = result["server"]
    has_auth = "username" in result

    if "socks5h" in server:
        proto = "SOCKS5H (remote DNS)"
    elif "socks5" in server:
        proto = "SOCKS5"
    elif "https" in server:
        proto = "HTTPS"
    else:
        proto = "HTTP"

    auth_str = "With certification" if has_auth else "No certification"

    return True, f"{proto} {auth_str}"
