# 🖨️ Ứng Dụng In Ấn Thông Minh

Hệ thống in ấn tự động với hỗ trợ máy in 1 mặt & 2 mặt, chế độ booklet A5, và hướng dẫn trực quan cho thao tác thủ công.

## ✨ Tính Năng

- **Tự động phát hiện máy in** với khả năng duplex
- **Hỗ trợ nhiều định dạng file**: DOC, DOCX, PDF, JPG, PNG
- **Chế độ in 2 mặt thường**: Tự động xử lý portrait (lật cạnh dài) và landscape (lật cạnh ngắn)
- **Chế độ Booklet A5**: In 4 trang A5 trên 2 mặt giấy A4 (hoàn hảo cho in sách nhỏ)
- **Manual Duplex thông minh**: Hướng dẫn trực quan bằng animation cho máy in 1 mặt
- **Giao diện đẹp**: Dark theme với glassmorphism effects

## 📋 Yêu Cầu

- **Windows** (Windows 10/11)
- **.NET 8 SDK** ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Microsoft Office** (Word) - cần thiết cho Word Interop
- Trình duyệt web hiện đại (Chrome, Edge, Firefox)

## 🚀 Cài Đặt & Chạy

### Backend

```powershell
# Di chuyển vào thư mục backend
cd backend

# Build project
dotnet build

# Chạy server
dotnet run
```

Backend sẽ chạy trên `http://localhost:8787`

### Frontend

Simply mở file `frontend/index.html` bằng trình duyệt, hoặc dùng Live Server:

```powershell
# Nếu có Python
cd frontend
python -m http.server 8080

# Hoặc dùng Live Server extension trong VS Code
```

Sau đó truy cập `http://localhost:8080`

## 📖 Hướng Dẫn Sử Dụng

### 1. **Chọn Máy In**
   - Danh sách máy in tự động load khi mở app
   - Máy in mặc định được chọn sẵn
   - Badge màu cho biết máy in hỗ trợ 2 mặt hay không

### 2. **Tải File In**
   - Kéo thả file vào khung upload
   - Hoặc click để chọn file
   - File tự động convert sang PDF nếu cần

### 3. **Chọn Chế Độ In**
   - **In 2 Mặt Thường**: Cho tài liệu thông thường
   - **Chế Độ Sách A5**: Tạo sách nhỏ từ giấy A4

### 4. **Bắt Đầu In**
   - Click "Bắt Đầu In"
   - Nếu dùng máy in 1 mặt, làm theo hướng dẫn quay giấy

## 🏗️ Kiến Trúc

```
myPrinter/
├── backend/              # .NET 8 Web API
│   ├── Models/          # Data models
│   │   └── PrintModels.cs
│   ├── Services/        # Core services
│   │   ├── WordInteropService.cs       # Word COM automation
│   │   ├── PrinterManagementService.cs # WMI printer detection
│   │   └── PrintAlgorithmService.cs    # Print logic
│   └── Program.cs       # API endpoints
│
└── frontend/            # Web application
    ├── index.html       # Main HTML
    ├── styles.css       # Premium styling
    └── app.js           # Application logic
```

## 🔧 API Endpoints

- `GET /api/printers` - Danh sách máy in
- `POST /api/upload` - Tải file lên
- `POST /api/convert` - Convert file sang PDF
- `POST /api/print` - Bắt đầu in
- `POST /api/print/continue` - Tiếp tục in (manual duplex)

## 🎯 Thuật Toán Booklet

Booklet mode tính toán thứ tự trang để khi gấp đôi giấy A4 thành sách A5, các trang theo đúng thứ tự:

**Ví dụ với 8 trang:**
- Tờ 1 Mặt trước: `[8, 1]` (phải, trái)
- Tờ 1 Mặt sau: `[2, 7]`
- Tờ 2 Mặt trước: `[6, 3]`
- Tờ 2 Mặt sau: `[4, 5]`

Sau khi in và gấp đôi → Sách A5 với trang `1,2,3,4,5,6,7,8` theo thứ tự chính xác.

## ⚠️ Lưu Ý

1. **Manual Duplex**: Làm chính xác theo hướng dẫn trên màn hình
   - **Portrait (dọc)**: Lật theo chiều cạnh dài (↕️)
   - **Landscape (ngang)**: Lật theo chiều cạnh ngắn (↔️)

2. **Booklet Mode**: Tài liệu sẽ được padding thành bội số của 4 trang

3. **File Size**: Giới hạn upload 100MB

## 🐛 Khắc Phục Sự Cố

**Không thấy máy in?**
- Đảm bảo máy in đã được cài đặt và online
- Thử refresh trang

**File không upload được?**
- Kiểm tra định dạng file (chỉ hỗ trợ doc, docx, pdf, jpg, png)
- Kiểm tra kích thước file < 100MB

**Lỗi Word Interop?**
- Đảm bảo đã cài Microsoft Office
- Chạy app với quyền Administrator

## 📝 License

MIT License - Free to use and modify

## 👨‍💻 Phát Triển Bởi

Antigravity AI Assistant - Advanced Print Management System
