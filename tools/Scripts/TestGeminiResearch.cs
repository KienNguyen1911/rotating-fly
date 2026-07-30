using System;
using System.IO;
using System.Threading.Tasks;
using AssetAutomator.Services;

namespace AssetAutomator.Scripts
{
    /// <summary>
    /// Test runner for testing GeminiApiService:
    /// 1. Get list of available Gems.
    /// 2. Deep Research a specific topic using Gem 25aa53b3798a.
    /// 3. Save the result to a markdown/txt file in Outputs directory.
    /// </summary>
    public static class TestGeminiResearch
    {
        public static async Task RunTestAsync()
        {
            Console.WriteLine("============================================================");
            Console.WriteLine(" TEST SUITE: GEMINI WEBAPI RESEARCH & GEMS LISTING");
            Console.WriteLine("============================================================\n");

            var geminiService = new GeminiApiService("http://localhost:8000");

            // 1. Get Gems list
            Console.WriteLine("1. Lấy danh sách Gemini Gems (GET /api/gems)...");
            var gems = await geminiService.GetGemsAsync(includeHidden: true);
            Console.WriteLine($"[OK] Tìm thấy {gems.Count} Gems:");

            string targetGemId = "25aa53b3798a";
            bool foundTarget = false;

            for (int i = 0; i < gems.Count; i++)
            {
                var gem = gems[i];
                string flag = gem.id.Equals(targetGemId, StringComparison.OrdinalIgnoreCase) ? " (===> TARGET GEM)" : "";
                if (gem.id.Equals(targetGemId, StringComparison.OrdinalIgnoreCase)) foundTarget = true;
                Console.WriteLine($"   {i + 1:D2}. [{gem.id}] {gem.name}{flag}");
            }

            if (!foundTarget)
            {
                Console.WriteLine($"\n[LƯU Ý] Gem ID '{targetGemId}' có thể là Custom Gem riêng, gửi trực tiếp qua API.");
            }

            // 2. Send Deep Research Chat
            string topicPrompt = @"Nghiên cứu chi tiết chủ đề: Thấu Hiểu Bản Ngã & Tiềm Thức (Self-Discovery & Unconscious Mind)
(Phong cách: Chiêm nghiệm, chiều sâu, khám phá bản thân)
Nội dung: Khai thác các khái niệm tâm lý học kinh điển (như của Carl Jung, Sigmund Freud...) nhưng được đơn giản hóa thành các câu chuyện tự nghiệm dành cho đêm muộn.

Gợi ý tiêu đề Video/Shorts:
- 'Vì sao ban đêm lại khiến con người cảm thấy cô đơn hơn?'
- 'Khám phá ''Shadow Self'' (Bản ngã bóng tối): Mặt khuất mà bạn luôn giấu kín'
- 'Tại sao chúng ta hay tự tổn thương chính mình trong vô thức?'

Lý do chọn: Ban đêm là thời điểm con người sống thật nhất với cảm xúc của mình và dễ mở lòng tiếp nhận những chủ đề sâu lắng.

Yêu cầu: Hãy phân tích sâu sắc chủ đề trên và viết bản Kịch bản (Transcript) video chi tiết, lôi cuốn bằng tiếng Việt.";

            Console.WriteLine($"\n2. Gửi request Deep Research tới Gem [{targetGemId}]...");
            Console.WriteLine("   Đang chờ phản hồi từ Gemini WebAPI (vui lòng đợi 15-45s)...");

            try
            {
                var response = await geminiService.SendChatAsync(
                    message: topicPrompt,
                    gemId: targetGemId,
                    deepResearch: true
                );

                Console.WriteLine($"\n[OK] Đã nhận phản hồi thành công! Session ID: {response.session_id}");

                // 3. Save to file
                string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
                Directory.CreateDirectory(outputDir);
                string filePath = Path.Combine(outputDir, "gemini_research_result.md");

                string markdown = $"# BÁO CÁO NGHIÊN CỨU & KỊCH BẢN (GEMINI DEEP RESEARCH)\n" +
                                 $"**Gem ID**: `{targetGemId}`\n" +
                                 $"**Thời gian**: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n\n---\n\n" +
                                 response.text;

                await File.WriteAllTextAsync(filePath, markdown, System.Text.Encoding.UTF8);
                Console.WriteLine($"\n3. [SUCCESS] Đã lưu kết quả nghiên cứu vào file:\n   👉 {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR] Lỗi thực thi Deep Research: {ex.Message}");
            }
        }
    }
}
