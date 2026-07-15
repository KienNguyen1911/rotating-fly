import base64
import time
import requests
import os

# Base configuration for ngrok G-Labs URL
BASE_URL = "https://mango-darn-crafter.ngrok-free.dev"
API_KEY = "EuXIJ7dMgmuOiKlQz2mc74kxe4yXJisgfezFODFvtl8"  # Replace with the actual API key from G-Labs Webhook tab
HEADERS = {
    "X-API-Key": API_KEY,
    "Content-Type": "application/json"
}

def to_base64_data_uri(file_path):
    ext = os.path.splitext(file_path)[1].lower().replace(".", "")
    if ext not in ["png", "jpg", "jpeg", "webp"]:
        ext = "png"
    mime_type = f"image/{ext}" if ext != "jpg" else "image/jpeg"
    with open(file_path, "rb") as f:
        encoded = base64.b64encode(f.read()).decode("utf-8")
    return f"data:{mime_type};base64,{encoded}"

def generate_image(prompt, reference_image_path=None):
    payload = {
        "prompt": prompt,
        "model": "nano_banana_2",
        "aspect_ratio": "16:9",
        "reference_images": []
    }

    if reference_image_path and os.path.exists(reference_image_path):
        print(f"Loading reference image: {reference_image_path}")
        payload["reference_images"].append(to_base64_data_uri(reference_image_path))

    # Send POST request to G-Labs generate endpoint
    url = f"{BASE_URL.rstrip('/')}/api/image/generate"
    print(f"Sending request to {url}...")
    response = requests.post(url, json=payload, headers=HEADERS)
    
    if response.status_code != 202:
        print(f"Error submitting task (HTTP {response.status_code}): {response.text}")
        return

    res_json = response.json()
    task_id = res_json.get("task_id")
    poll_url = res_json.get("poll_url")
    print(f"Task queued. Task ID: {task_id}, Poll URL: {poll_url}")

    # Polling the status endpoint
    status_url = f"{BASE_URL.rstrip('/')}/api/status/{task_id}"
    print(f"Checking status at {status_url}...")
    
    while True:
        status_res = requests.get(status_url, headers=HEADERS)
        if status_res.status_code != 200:
            print(f"Error getting status: {status_res.text}")
            break
        
        status_data = status_res.json()
        status = status_data.get("status")
        print(f"Current status: {status}")
        
        if status == "completed":
            results = status_data.get("results", [])
            print(f"Completed! Download URLs: {results}")
            return results
        elif status == "failed":
            err_code = status_data.get("error_code")
            err_msg = status_data.get("error")
            print(f"Task failed (code {err_code}): {err_msg}")
            break
        
        time.sleep(3)

if __name__ == "__main__":
    prompt = "a beautiful futuristic city at sunset, 16:9"
    if API_KEY == "YOUR_API_KEY_HERE" or not API_KEY:
        print("Please configure your G-Labs API_KEY in the script first.")
    else:
        print(f"Starting test image generation with prompt: '{prompt}'")
        results = generate_image(prompt)
        if results:
            print("Successfully retrieved generated images:")
            for r in results:
                print(f" - {r}")
        else:
            print("Failed to generate image.")
