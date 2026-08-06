# Tài liệu AssetAutomator

Tài liệu trong thư mục này phản ánh trạng thái mã nguồn được rà soát ngày **05/08/2026**. Các kế hoạch cũ trong `improve-docs/` và tài liệu migration WinUI được giữ như lịch sử; không nên dùng chúng làm nguồn trạng thái hiện tại.

## Bắt đầu nhanh

- [Hướng dẫn cài đặt, build và chạy](../HOW-TO-RUN.md)
- [Tổng quan tính năng đã triển khai](./product/FEATURES.md)
- [Hạn chế và các điểm cần cải thiện](./product/LIMITATIONS.md)
- [Kiến trúc hiện tại](./engineering/ARCHITECTURE.md)
- [Đánh giá code quality](./engineering/CODE-QUALITY.md)
- [Lộ trình cải thiện kỹ thuật](./engineering/IMPROVEMENT-ROADMAP.md)

## Phân loại trạng thái

- **Đã triển khai**: có mã nguồn và đã được nối vào WinUI hoặc pipeline đang dùng.
- **Triển khai một phần**: có mã nguồn/UI nhưng còn placeholder, phụ thuộc ngoài hoặc chưa đủ bằng chứng để coi là production-ready.
- **Legacy**: còn trong mã nguồn để tương thích, không phải hướng phát triển chính.
- **Đề xuất**: chưa triển khai; là phương án cải thiện được đề nghị sau rà soát.

## Nguồn tài liệu khác

- `FLOW-API.md`: tài liệu tích hợp Google Flow API chuyên sâu.
- `docs/FLOW-FlowLocal-AutoStart.md`: ghi chú luồng tự khởi động Flow Local.

## Quy tắc cập nhật docs

Khi thay đổi tính năng:

1. Cập nhật `product/FEATURES.md` nếu hành vi người dùng thay đổi.
2. Cập nhật `product/LIMITATIONS.md` khi thêm hoặc xử lý known issue.
3. Cập nhật `engineering/ARCHITECTURE.md` khi dependency/layer/pipeline thay đổi.
4. Cập nhật `engineering/CODE-QUALITY.md` và roadmap khi xử lý technical debt.
5. Không ghi “build pass”, “test pass” hoặc “production-ready” nếu chưa chạy kiểm chứng trong cùng thay đổi.
