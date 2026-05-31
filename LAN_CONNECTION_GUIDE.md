# Hướng dẫn kết nối Demo — LAN & Internet

## Yêu cầu

- Máy chủ cần chạy **MasterServer** và **DrawServer** trước khi client kết nối
- MySQL đang chạy trên máy server

---

## Chế độ LAN (cùng mạng WiFi)

### Máy SERVER:

```powershell
.\restore-lan.ps1
```

Sau đó trong Visual Studio: **Rebuild DrawServer** → Start MasterServer → Start DrawServer

### Máy CLIENT (bạn bè):

Sửa `DrawClient/config.ini`:
```ini
[Server]
MasterServerIp=192.168.2.6
MasterServerPort=5274
```

> Kiểm tra IP máy server bằng `ipconfig` nếu IP khác

---

## Chế độ Internet (khác mạng, dùng serveo.net)

### Máy SERVER:

**Bước 1** — Mở PowerShell tại thư mục project, chạy:
```powershell
.\start-internet.ps1
```
Script tự mở 2 cửa sổ SSH tunnel và cập nhật config.

**Bước 2** — Chờ 2 cửa sổ SSH hiện thông báo:
```
Forwarding TCP connections from tcp://serveo.net:5274
Forwarding TCP connections from tcp://serveo.net:6001
```

**Bước 3** — Trong Visual Studio: **Rebuild DrawServer** → Start MasterServer → Start DrawServer

### Máy CLIENT (bạn bè):

Sửa `DrawClient/config.ini`:
```ini
[Server]
MasterServerIp=serveo.net
MasterServerPort=5274
```

Chạy `DrawClient.exe` → kết nối được qua Internet.

---

## Quay về LAN sau khi demo Internet

```powershell
.\restore-lan.ps1
```

Rồi **Rebuild DrawServer** và start lại.

---

## Lưu ý

| # | Lưu ý |
|---|---|
| 1 | Sau mỗi lần đổi chế độ phải **Rebuild DrawServer** |
| 2 | 2 cửa sổ SSH tunnel phải **giữ mở** suốt thời gian demo |
| 3 | Nếu SSH tunnel bị đứt → chạy lại `.\start-internet.ps1` |
| 4 | Kiểm tra server đang chạy: `netstat -an \| findstr "5274 6001"` |
