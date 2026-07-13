import requests

url = "http://localhost:8000/v1/images/edits"
headers = {
    "Authorization": "Bearer chatgpt2api"
}

# Let's use one of the existing jpg files
image_path = r"C:\Users\ngkie\Dev\AutoCreateImage\bin\Debug\net10.0-windows\Outputs\OBsAjFGSwUA\OBsAjFGSwUA_thumbnail.jpg"

files = {
    "image": ("OBsAjFGSwUA_thumbnail.jpg", open(image_path, "rb"), "image/jpeg")
}

data = {
    "model": "gpt-image-2",
    "prompt": "tạo một bức ảnh tương tự với phần văn bản được dịch sang ngôn ngữ 'en', kích thước ảnh 16:9",
    "n": "1"
}

print("Sending request...")
response = requests.post(url, headers=headers, files=files, data=data)
print(f"Status Code: {response.status_code}")
print("Response text:")
print(response.text)
