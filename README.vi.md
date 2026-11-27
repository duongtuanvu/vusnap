# VuSnap

![VuSnap logo](docs/images/logo.png)

Ứng dụng chụp màn hình và chú thích ảnh trên Windows, sử dụng C#, WPF và .NET 10. Phát triển độc lập bởi vudt, dựa trên [NinoMark v0.5.0](https://github.com/TokisakiNinoVn/NinoMark/commit/bab3f157d21a0ae52b2a966570c492b5f4c80da7).

[English](README.md) · [Bản phát hành](https://github.com/duongtuanvu/vusnap/releases) · [Báo lỗi](https://github.com/duongtuanvu/vusnap/issues)

## Chụp nâng cao

Chọn chế độ và hẹn giờ trong cửa sổ chính hoặc menu khay hệ thống. Repeat sử dụng vùng xuất ảnh thành công gần nhất trong phiên chạy nếu bố cục màn hình không đổi. Chụp cửa sổ lấy pixel đang hiển thị, có thể chứa cửa sổ che phía trước. Nhấn lại phím tắt để hủy hẹn giờ. Xem [nguồn mã tái sử dụng](docs/THIRD_PARTY.md).

## Tính năng

- Chụp bằng Alt+Shift+S, chọn vùng, đổi kích thước, kính lúp, lưới và khóa tỷ lệ.
- Chú thích trực tiếp: chữ, bút, đường thẳng, mũi tên, hình chữ nhật, elip, highlight, pixelate, đảo màu, đánh số, thước đo, phóng đại.
- Quản lý layer, nhân bản, xoay, lật và undo/redo cơ bản.
- Lưu PNG/JPEG, copy clipboard, tùy chỉnh phím tắt và toolbar.
- Tiếng Việt/Anh, giao diện sáng/tối, chạy nền, khởi động cùng Windows.
- Cấu hình tại `%AppData%\VuSnap\settings.json`, có thể đổi thư mục bằng `VUSNAP_DATA_DIR`.

## Build trên Windows

Cần .NET 10 SDK:

```powershell
dotnet build VuSnap.sln -c Release
dotnet run --project src/VuSnap/VuSnap.csproj
```

## Giấy phép

MIT. Giữ nguyên thông báo bản quyền tác giả gốc Tokisaki Nino và bổ sung vudt trong [LICENSE](LICENSE). VuSnap sử dụng logo và icon riêng; lịch sử nguồn tại [docs/UPSTREAM_CHANGELOG.md](docs/UPSTREAM_CHANGELOG.md).

## Chụp cuộn (v0.5.0)

Chọn **Chụp cuộn** → chọn cửa sổ WGC → kéo vùng nội dung cần cuộn trên ảnh xem trước. Kích hoạt cửa sổ nguồn rồi cuộn xuống từ từ, hoặc bật **Tự cuộn** và đưa con trỏ vào vùng chọn. Tự cuộn dừng khi không khớp ảnh; nếu chuyển ứng dụng hoặc đưa chuột ra ngoài, lệnh cuộn tạm dừng. Không di chuyển/đổi kích thước cửa sổ trong phiên. Bấm **Hoàn tất** để xem ảnh dài, copy hoặc lưu PNG/JPEG. Hủy bằng nút Hủy hoặc phím tắt chụp. Giới hạn ảnh kết quả 24 MP, tối đa 16.384 pixel chiều dài; phiên tối đa 180 giây. Chỉ ghép phần khớp, không đoán phần thiếu. Kết quả phụ thuộc nội dung và ứng dụng; nghiệm thu Windows thực tế còn mở.
