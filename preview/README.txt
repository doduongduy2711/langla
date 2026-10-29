QLTK C# WinForms — bản thử giao diện .NET Framework 4.8 x64

1. Cập nhật QLTK lên v289 hoặc mới hơn, mở lại QLTK một lần, bật Socket trong phần Socket / Proxy.
   Giữ QLTK đó chạy làm bộ điều phối, có thể thu nhỏ cửa sổ cũ.
2. Mở QLTK-CSharp-Preview.exe, chọn thư mục Data của QLTK hiện tại.
3. Cuộn, tìm kiếm, tích chọn tài khoản trong cửa sổ mới.
4. Sau khi bộ điều phối nạp SocketApi.ps1 mới, cửa sổ thử còn có:
   trạng thái tài khoản, Auto tất cả, dừng Auto tất cả, tắt/ẩn/hiện đã chọn.
   Nếu đang dùng bộ điều phối cũ, các nút điều khiển sẽ bị khóa, vẫn xem được danh sách.
   Các nút sẽ tự bật sau khi bộ điều phối nạp kênh mới và bản thử nhận được trạng thái.

Có thể chạy QLTK-CSharp-Preview.exe --demo để thử bảng 5.000 tài khoản giả.
Hoặc QLTK-CSharp-Preview.exe --data "D:\QLTK\Data" để chọn thư mục trực tiếp.

Đây là giao diện C# độc lập, không tham chiếu hoặc nhúng PowerShell.
Các luồng auto và tab cấu hình vẫn do QLTK hiện tại điều phối trong giai đoạn thử.
Mở riêng bản thử không tự khởi động auto, không mở game và không sửa dữ liệu.
Đóng bản thử không tắt QLTK cũ hoặc game. Không tự gửi lại lệnh khi mất phản hồi.
Không chạy thêm một bộ điều phối trên cùng danh sách tài khoản.

Để đối chiếu độ mượt: cuộn/tìm kiếm trong cửa sổ mới khi Auto tất cả chuyển đợt.
Giao diện vẫn có thể phản hồi trong khi thông tin từ bộ điều phối đang cũ/chờ.
Chưa thể kết luận toàn bộ hệ thống ổn định hơn chỉ từ việc cuộn mượt.
