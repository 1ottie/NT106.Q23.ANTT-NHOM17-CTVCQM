# Hướng Dẫn Đổi IP/Port Khi Chuyển Mạng LAN

## IP Hiện Tại: `10.45.27.103`

Khi chuyển sang mạng LAN khác, **chỉ cần sửa 2 file `config.ini`** rồi chạy lại — **KHÔNG cần build lại**.

---

## Bước 1 — Lấy IP mới

Trên **máy chủ** (máy chạy MySQL + MasterServer + DrawServer), mở CMD:
```
ipconfig
```
Tìm dòng `IPv4 Address` trong card mạng đang dùng (Wi-Fi hoặc Ethernet).  
Ví dụ kết quả: `10.95.48.92`

---

## Bước 2 — Sửa file cấu hình (không cần build lại)

### File 1: `DrawClient/config.ini`  
> Sao chép file này kèm theo `DrawClient.exe` cho **máy khách**.

```ini
[Server]
MasterServerIp=10.95.48.92   # <-- đổi thành IP mới
MasterServerPort=5274
```

---

### File 2: `DrawServer/config.ini`  
> Đặt file này cạnh `DrawServer.exe` trên **máy chủ**.

```ini
[Server]
NodeIp=10.95.48.92           # <-- IP của máy đang chạy DrawServer
NodePort=6001
MasterServerIp=10.95.48.92   # <-- IP của máy đang chạy MasterServer
MasterServerPort=5274

[Database]
DbConnectionString=server=localhost;database=online_Drawing_DB;user=root;password=182806
```

> Nếu MasterServer và DrawServer chạy **cùng 1 máy** thì `NodeIp` = `MasterServerIp` = IP của máy đó.

---

## Vị trí file config.ini sau khi build

| Dự án | Vị trí `config.ini` |
|-------|---------------------|
| DrawClient | `DrawClient\bin\Release\config.ini` (hoặc `bin\Debug\`) |
| DrawServer | `DrawServer\bin\Release\config.ini` (hoặc `bin\Debug\`) |

File này được tự động copy vào thư mục output mỗi khi build.  
Khi phát hành cho máy khách, copy toàn bộ thư mục `bin\Release\` — file `config.ini` đã có sẵn ở đó.

---

## Bước 3 — Chạy lại (không cần build)

Chỉ cần **sửa file `config.ini`** và chạy lại exe. Không cần mở Visual Studio.

---

## Thứ Tự Khởi Động

1. Khởi động **MySQL** trên máy chủ
2. Chạy **MasterServer** (`dotnet run` hoặc exe publish)
3. Chạy **DrawServer** (`DrawServer.exe`)
4. Chạy **DrawClient** trên các máy khách

---

## Cấu Trúc Hệ Thống

```
Máy chủ (ví dụ IP: 10.95.48.92)
├── MySQL              :3306  ← database
├── MasterServer       :5274  ← Web API (HTTP)
└── DrawServer         :6001  ← Socket vẽ (TCP)

Máy khách (bất kỳ IP nào trong LAN)
└── DrawClient
    └── config.ini: MasterServerIp=10.95.48.92
```

---

## Mở Firewall (chỉ làm 1 lần trên máy chủ)

Chạy CMD với quyền **Admin**:
```bat
netsh advfirewall firewall add rule name="MasterServer" dir=in action=allow protocol=TCP localport=5274
netsh advfirewall firewall add rule name="DrawServer"   dir=in action=allow protocol=TCP localport=6001
```

---

## Lưu Ý

- `DbConnectionString` dùng `server=localhost` vì MySQL chạy cùng máy với DrawServer — **không cần đổi**.
- `MasterServer` lắng nghe trên `0.0.0.0:5274` nên tự động chấp nhận kết nối từ mọi máy trong LAN — **không cần cấu hình thêm**.
- Nếu `config.ini` không tồn tại, cả DrawClient lẫn DrawServer sẽ dùng IP mặc định `10.45.27.103`.
