QLTK C# WinForms — bản thử đầy đủ tab, .NET Framework 4.8 x64 (v290)

Giải nén toàn bộ gói vào thư mục riêng. Mở QLTK-CSharp-Preview.exe,
chọn thư mục Data hiện có. Giữ nguyên QLTK và QLTK-Backend.exe trong gói.
Có thể dùng --data "D:\QLTK\Data" hoặc --demo để xem 5.000 tài khoản giả.

Toàn bộ 26 tab cùng trang Điều khiển chung được dựng bằng C#.
Các nút dùng lại đúng bộ xử lý QLTK; cấu hình lưu vào Data đã chọn.
Một số hộp thoại phụ do bộ điều phối hiển thị; nút Cửa sổ phụ đưa chúng lên trước.

Nếu chưa có QLTK chạy, bản thử mở bộ điều phối ẩn, không tự bật hoạt động hay game.
Nếu QLTK đang chạy, bản thử kết nối phiên đó. Phiên cũ cần cập nhật v290 và
mở lại để nạp kênh đầy đủ; không mở thêm bộ điều phối trùng Data.
Đóng cửa sổ C# không tắt game hoặc bộ điều phối. Bộ điều phối có biểu tượng khay
hệ thống để mở cửa sổ chẩn đoán hoặc thoát khi cần.

Giao diện chính là C#; nghiệp vụ vẫn dùng lại PowerShell trong bộ điều phối riêng.
Đây chưa phải bản viết lại toàn bộ nghiệp vụ sang C#.
Chỉ tải trang đang xem, cập nhật giá trị thay đổi và lấy vùng bảng đang cuộn.
Không tự gửi lại thao tác khi mất phản hồi. Gói không kèm Data hoặc tài khoản.

Từ v291: nút Cập nhật giao diện tải bản C# mới nhất từ GitHub, kiểm tra SHA-256 rồi mở lại riêng cửa sổ C#. Data, game và bộ điều phối giữ nguyên. Bộ điều phối vẫn cập nhật qua nút Cập nhật QLTK của nó.
