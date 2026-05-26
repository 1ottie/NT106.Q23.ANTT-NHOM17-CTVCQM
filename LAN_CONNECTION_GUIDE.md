# Hướng Dẫn Đổi IP Khi Chuyển Mạng LAN

## IP Hiện Tại: `192.168.2.2`

Khi chuyển sang mạng LAN khác, cần đổi IP ở **4 chỗ** sau:

---

## Danh Sách Các File Cần Đổi IP

### 1. `DrawServer/ServerSocket.cs` — dòng 19
```csharp
private string connectionString =
    "server=192.168.2.2;database=online_Drawing_DB;user=root;password=182806";
```
Đổi `192.168.2.2` thành IP mới của máy chủ.

---

### 2. `DrawServer/Program.cs` — dòng 13 và dòng 20
```csharp
// Dòng 13 - connection string
string connectionString = "server=192.168.2.2;database=online_Drawing_DB;user=root;password=182806";

// Dòng 20 - constructor ServerSocket
ServerSocket server = new ServerSocket(masterIp: "192.168.2.2", masterPort: 5274, nodeIp: "192.168.2.2");
```
Đổi cả 2 chỗ `192.168.2.2` thành IP mới.

---

### 3. `MasterServer/appsettings.json` — dòng 10
```json
"DefaultConnection": "server=192.168.2.2;database=online_Drawing_DB;user=root;password=182806"
```
Đổi `192.168.2.2` thành IP mới của máy chủ.

---

### 4. `DrawClient/ViewModels/LoginViewModel.cs` — dòng 35
```csharp
ServerIp = "192.168.2.2";
```
Đổi thành IP mới để giao diện Login tự điền sẵn IP đúng.

---

## Cách Lấy IP Mới

Trên máy chủ (máy chạy MySQL + MasterServer + DrawServer), mở CMD và chạy:
```
ipconfig
```
Tìm dòng `IPv4 Address` trong card mạng đang dùng (Wi-Fi hoặc Ethernet).

---

## Cấu Trúc Hệ Thống

```
Máy chủ (IP: 192.168.2.2)
├── MySQL              :3306  ← database
├── MasterServer       :5274  ← Web API (HTTP)
└── DrawServer         :6001  ← Socket vẽ

Máy khách (bất kỳ IP nào trong LAN)
└── DrawClient
    └── Nhập Server IP: 192.168.2.2, Port: 5274 khi Login
```

---

## Các Port Cần Mở Firewall (trên máy chủ)

| Port | Dùng cho |
|------|----------|
| 3306 | MySQL (nếu DrawServer chạy trên máy khác) |
| 5274 | MasterServer API |
| 6001 | DrawServer Socket |

Lệnh mở firewall (chạy CMD với quyền Admin):
```bat
netsh advfirewall firewall add rule name="MySQL" dir=in action=allow protocol=TCP localport=3306
netsh advfirewall firewall add rule name="MasterServer" dir=in action=allow protocol=TCP localport=5274
netsh advfirewall firewall add rule name="DrawServer" dir=in action=allow protocol=TCP localport=6001
```

---

## Yêu Cầu MySQL Cho Remote Connection

MySQL mặc định chỉ nhận kết nối từ `localhost`. Để máy khác kết nối được:

**Bước 1:** Sửa file `my.ini` (thường ở `C:\ProgramData\MySQL\MySQL Server X.X\`):
```ini
bind-address = 0.0.0.0
```

**Bước 2:** Trong MySQL Workbench hoặc CMD, cấp quyền remote cho user root:
```sql
CREATE USER IF NOT EXISTS 'root'@'%' IDENTIFIED BY '182806';
GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION;
FLUSH PRIVILEGES;
```

**Bước 3:** Restart MySQL service.

---

## Thứ Tự Khởi Động

1. Khởi động **MySQL** trên máy chủ
2. Khởi động **MasterServer** (chạy `dotnet run` hoặc file exe)
3. Khởi động **DrawServer** (chạy file exe)
4. Khởi động **DrawClient** trên các máy khách, nhập IP `192.168.2.2` và Port `5274`
