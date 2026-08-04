"""
Launcher for Google Flow Extended API Server.
"""

import uvicorn


def main() -> None:
    print("Starting Google Flow Extended API on http://127.0.0.1:8787 ...")
    uvicorn.run(
        "google_flow_ext.api.app:app",
        host="0.0.0.0",
        port=8787,
        reload=False,
    )


if __name__ == "__main__":
    main()
