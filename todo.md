tôi muốn cải tiến workflow tạo video như sau:
- step 1: gợi ý chủ đề: làm theo kênh youtube => chọn chủ đề
- step 2: đưa chủ đề vào gemini gem tương ứng (chọn gemini gem tùy chỉnh) => gemini sẽ dùng chế độ DEEP RESEARCH + EXTENDED để nghiên cứu chủ đề đó => sau đó sẽ tạo transcript
- step 3: từ transcript có được từ step 2, sẽ dùng ai84 để tạo voiceover.mp3 và srt
- step 4: từ srt + transcript, sẽ dùng gemini gem "Scene Creator" (có thể tùy chỉnh) để tạo json 
{
    "video_title": "Tâm Lý Học Đêm Muộn: Tại Sao Bạn Lại Trì Hoãn Giấc Ngủ",
    "scene_count": 58,
    "scenes": [
        {
            "scene": 1,
            "id": "scene_001",
            "time": {
                "start": "00:00:00,099",
                "end": "00:00:11,679",
                "duration": 11.58
            },
            "transcript": "Hello. If you are lying in the dark right now, staring at the ceiling, feeling the heavy weight of the world resting on your shoulders, it is completely okay.",
            "image_prompt": "Close-up of @character lying awake on its back. Glowing heavy weights press on its chest. minimalist golden neon line art stickman doodle style, dark 2D lo-fi aesthetic, pitch black background."
        },
        ...
    ]
}

- step 5: từ json, sẽ sử dụng tool để tạo ảnh tương ứng từ image_prompt