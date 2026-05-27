# CTVCQM – Project Code Preview

## Architecture

```
CTVCQM Solution
├── DrawClient      WPF desktop app (.NET 4.8)   – end-user drawing tool
├── DrawServer      TCP socket node (.NET 4.8)    – real-time sync server
└── MasterServer    ASP.NET Core API (.NET 8)     – auth, room & node management
```

**Tech stack:** WPF + MVVM · JWT auth · BCrypt · MySQL + Dapper · Google Vision OCR · TCP sockets

---

## DrawClient — WPF Desktop Application

### Entry / Shell

| File | Role |
|------|------|
| `App.xaml.cs` | Sets software rendering mode; global exception handler (Vietnamese messages) |
| `MainWindow.xaml.cs` | Root window; `ContentControl` bound to `CurrentView`; data-template routes ViewModel→View |
| `AppConfig.cs` | Reads `config.ini`; defaults to `10.45.27.103:5274` |
| `ClientSocket.cs` | Singleton TCP client; dual-phase connect (Master→Node); line-delimited JSON protocol; 35 s timeout |

### ViewModels (MVVM)

| File | Role |
|------|------|
| `ViewModels/MainViewModel.cs` | Navigation controller: Login → Lobby → Canvas |
| `ViewModels/LoginViewModel.cs` | HTTP POST `/api/auth/login`; stores JWT + user_id |
| `ViewModels/DashboardViewModel.cs` | Room list, create/join room; receives `nodeIp:nodePort` from Master |
| `ViewModels/Canvas/CanvasViewModel.cs` | Core drawing logic (1555 lines): tools, shapes, undo/redo, replay, network sync |
| `ViewModels/Canvas/ToolbarViewModel.cs` | Tool/color/size state; exclusive popup modes |
| `ViewModels/Canvas/ChatViewModel.cs` | Chat messages with 15-min date separators |

### Services

| File | Role |
|------|------|
| `Services/UndoRedoManager.cs` | Per-user undo/redo stacks; max 200 history; `UndoById`/`RedoById` by actionId |
| `Services/OcrService.cs` | Base64-encodes canvas region → POST `/api/ocr/recognize` |

### Models (DTOs)

| Model | Description |
|-------|-------------|
| `DrawMessage` | Network protocol message (all canvas ops share this schema) |
| `DrawAction` | Local action with unique ID, `IsUndone` flag, metadata |
| `ChatMessage` | Chat UI model with initials extraction, timestamp, separator logic |
| `LoginRequest/Response` | Auth DTOs with JWT token and user info |
| `MasterRequest/Response` | Master Server protocol for room negotiation |
| `Room` | Room info (id, name, max_users, is_private, player_count, node) |
| `User` | User profile (user_id, username, email) |
| `HistoryMessage` | HISTORY packet wrapper containing list of DrawMessages |
| `RelayCommand` | ICommand implementation for MVVM binding |

### Views / UserControls

| File | Role |
|------|------|
| `Views/UserControls/LoginScreen.xaml` | Login + Register toggle form |
| `Views/UserControls/Dashboard.xaml` | Room list grid, create/join panels, server status sidebar |
| `Views/UserControls/Canvas.xaml` | InkCanvas + toolbar + chat/participants sidebar + replay controls |

### Value Converters

| Converter | Converts |
|-----------|----------|
| `BoolToVisibilityConverter` | `bool` → `Visibility.Visible/Collapsed` |
| `BoolToPlayIconConverter` | `IsPlaying` → Play triangle / Stop square SVG path |
| `HexToBrushConverter` | Hex string ↔ WPF `SolidColorBrush` |

---

## DrawServer — TCP Node Server

### Files

| File | Role |
|------|------|
| `Program.cs` | Starts `RoomCleanupService` + `ServerSocket` |
| `AppConfig.cs` | Reads `config.ini`; defaults NodeIp:6001, MasterIp:5274 |
| `DrawMessage.cs` | Universal JSON message schema shared with client |
| `ServerSocket.cs` | Core 750-line server: connection mgmt, message routing, DB ops, broadcasts |
| `RoomCleanupService.cs` | Every 24 h: cascade-deletes rooms inactive >28 days |

### ServerSocket Key Behaviors

- **Registration**: On startup → `POST /api/node/register` to MasterServer
- **Rooms map**: `ConcurrentDictionary<roomId, ConcurrentDictionary<TcpClient, byte>>`
- **PING heartbeat**: every 30 s to detect dead connections
- **JOIN**: sends existing members list, full HISTORY + CHAT_HISTORY, broadcasts new user to room
- **DRAW / ERASE / SHAPE / TEXT**: broadcasts to room (except sender), saves to `DrawActions` table
- **LASER**: broadcast-only, no DB persistence
- **UNDO / REDO**: saves action metadata, broadcasts for sync
- **LEAVE / disconnect**: sets `is_online=0` in DB, posts status update to Master, broadcasts LEAVE

---

## MasterServer — ASP.NET Core REST API (port 5274)

### Endpoints

```
POST /api/auth/register            Create account (BCrypt hash)
POST /api/auth/login               Verify password → JWT token + user info

POST /api/node/register            DrawServer calls on startup → upsert node record
POST /api/node/heartbeat           Keep node ACTIVE
GET  /api/node/list                List all nodes

POST /api/room/create   [JWT]      Select active node → INSERT Room+Member → return nodeIp:nodePort
POST /api/room/join     [JWT]      Verify password (if private) → return nodeIp:nodePort
POST /api/room/leave    [JWT]      Set is_online=0
POST /api/room/update-status       DrawServer calls after JOIN/LEAVE/DISCONNECT (no JWT)
GET  /api/room/list     [JWT]      Rooms with online player counts
GET  /api/room/{id}/members [JWT]  Members list

POST /api/ocr/recognize            Forward Base64 image → Google Vision API → return text
```

### Services / Helpers

| File | Role |
|------|------|
| `Services/AuthService.cs` | Register/login with BCrypt |
| `Services/NodeService.cs` | Node upsert, heartbeat, list |
| `Services/RoomService.cs` | Room CRUD + member management |
| `Helpers/JwtHelper.cs` | HS256 JWT generation |
| `Data/DbConnection.cs` | MySQL connection pool wrapper (Dapper) |

---

## Database Schema (MySQL)

```sql
Users       (user_id, username, password_hash, email, created_at)
Nodes       (node_id, ip_address, port, status, last_heartbeat, current_users)
Rooms       (room_id, room_name, is_private, password_hash, owner_id, node_id, max_users, created_at)
RoomMembers (id, user_id, room_id, is_online, role, joined_at)
DrawActions (action_id, user_id, room_id, type, data JSON, created_at)
Messages    (message_id, user_id, room_id, content, created_at)
```

---

## TCP Protocol: DrawServer ↔ Client

**Format:** newline-terminated JSON — `{...}\n`

| Direction | Type | Key Fields |
|-----------|------|------------|
| Client→Server | `JOIN` | roomId, userId, username |
| Client→Server | `DRAW` | x1,y1,x2,y2, color, thickness, penType |
| Client→Server | `ERASE` | x1,y1,x2,y2, thickness |
| Client→Server | `SHAPE` | shapeType, x1,y1,x2,y2, color, thickness |
| Client→Server | `TEXT` | x1,y1, text, fontSize, color |
| Client→Server | `DELETE_TEXT` | text object id |
| Client→Server | `LASER` | coordinates (broadcast only, no DB) |
| Client→Server | `CHAT` | username, text |
| Client→Server | `UNDO` | actionToUndoId |
| Client→Server | `REDO` | actionId |
| Client→Server | `TRANSFORM_SELECTION` | stroke indices + old/new bounds |
| Client→Server | `CLEAR` | clears canvas |
| Client→Server | `LEAVE` | roomId, userId |
| Server→Client | `PING` | heartbeat every 30 s |
| Server→Client | `HISTORY` | all past DrawActions on JOIN |
| Server→Client | `CHAT_HISTORY` | all past messages on JOIN |
| Server→Client | *(broadcast)* | echo of any message type from other users |

---

## Full Connection Flow

```
1. Login        Client  →  POST /api/auth/login          →  JWT token
2. Create/Join  Client  →  POST /api/room/create|join    →  nodeIp:nodePort
3. TCP Connect  Client  →  TCP connect to nodeIp:nodePort
4. Join room    Client  →  SEND { type:"JOIN", roomId, userId, username }
5. Sync         Server  →  SEND HISTORY + CHAT_HISTORY
6. Collaborate  Client ↔ Server ↔ All room clients (real-time broadcast)
7. Disconnect   Server  →  is_online=0 in DB
                        →  POST /api/room/update-status to Master
                        →  Broadcast LEAVE to room
```

---

## Inter-Server Communication

| Caller | Endpoint | When |
|--------|----------|------|
| DrawServer | `POST /api/node/register` | On startup |
| DrawServer | `POST /api/room/update-status` | After each user JOIN / LEAVE / disconnect |
| Client | `POST /api/auth/login` | Login |
| Client | `POST /api/room/create\|join` | Enter room (returns node address) |
| Client | TCP socket to nodeIp:nodePort | All real-time drawing |

---

## NuGet Dependencies

| Package | Used In |
|---------|---------|
| `MySql.Data 9.7.0` | DrawServer, MasterServer |
| `Newtonsoft.Json 13.0.4` | DrawClient, DrawServer |
| `BCrypt.Net` | MasterServer (password hashing) |
| `Dapper` | MasterServer (SQL queries) |
| `System.IdentityModel.Tokens.Jwt` | MasterServer (JWT) |
| `Google.Protobuf` | MasterServer (Vision API) |
