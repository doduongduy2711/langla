# QLTK

Trình quản lý tài khoản chạy trên Windows 64-bit, .NET Framework 4.8 trở lên và Windows PowerShell 5.1.

Repository chỉ chứa mã chương trình và bộ kiểm tra. Tài khoản, mật khẩu, proxy, cấu hình riêng và log luôn ở máy người dùng, không nằm trong bản phát hành. Game được cài riêng; chọn đường dẫn trong QLTK.

## Cập nhật trên các máy

Dùng repository công khai để máy khách tải bản phát hành mà không cần token GitHub. Trong QLTK, nhấp chuột phải vào **Cập nhật QLTK → Nguồn cập nhật GitHub**, nhập `owner/repo`. Bấm nút để tải bản phát hành mới nhất. Bản sửa hàm PowerShell được nạp trong phiên đang chạy; thay đổi bộ điều khiển đã nạp hoặc cấu trúc giao diện có thể cần mở lại QLTK. QLTK thông báo và không tự tắt game.

Gói tải được kiểm tra SHA-256 theo GitHub Release và từng tệp; chỉ tệp chương trình được ghi, có sao lưu/khôi phục khi ghi lỗi. Bản tải, bản sao lưu nằm trong `QLTK/.updates`. Khi tải bị lỗi, dữ liệu tài khoản giữ nguyên. Không đặt tài khoản/token trong `update-channel.json`. Repository riêng tư chưa hỗ trợ tải tự động.

## Kiểm tra và đóng gói

Trên Windows PowerShell:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File QLTK/AccountManager.ps1 -UiTest
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -Version 209 -Repository owner/repo
```

Build tạo gói cập nhật `QLTK-update.zip` và gói cài mới `QLTK-portable.zip`. Máy đang dùng bản cũ chưa có phần tải GitHub cần nhận gói mới một lần. Bản tải không thay thế QLTK.exe của phiên đang chạy.

Để phát hành: push tag `v209` (số phiên bản phải tăng cho các lần sau). GitHub Actions kiểm tra trên Windows, đóng gói và đưa hai ZIP lên GitHub Releases. Cần bật Actions và quyền workflow tạo Release. Mỗi bản phát hành có nguồn repo mặc định theo `github.repository`.

Launcher được build bằng reference assemblies .NET Framework 4.8, có QLTK.exe.config đi kèm. Gói QLTK-net48-launcher.zip chỉ chứa EXE và cấu hình runtime; đóng cửa sổ QLTK trước khi thay hai file cạnh QLTK.exe. Không cần thay thư mục Data. Nút cập nhật nguồn chương trình không thay launcher đang chạy.

## Portal OCR chạy riêng

Tải `OCR-Portal-portable.zip` trong Releases, giải nén vào thư mục mới và mở `MO-PORTAL.cmd`. Gói đã kèm Python và bộ OCR; không cần chép đè QLTK. Dữ liệu mặc định nằm trong `OCR-Portal/Data/samples`. Để duyệt ảnh QLTK đang có, mở `CHON-THU-MUC-ANH.cmd` và chọn thư mục ảnh; nhãn đã duyệt được lưu tại thư mục đó. Dùng `DUNG-PORTAL.cmd` để dừng phiên do gói này mở. Khi cổng 47662 đã có ứng dụng khác, portal chọn cổng local còn trống trong 47663–47671.
