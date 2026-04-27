CREATE DATABASE Online_Drawing_DB

USE Online_Drawing_DB

-- Bảng Users lưu trữ thông tin tài khoản
CREATE TABLE Users (
    user_id INT PRIMARY KEY AUTO_INCREMENT, -- tự động tăng id
    username VARCHAR(50) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL, -- mật khẩu đã mã hoá
    email VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Bảng Nodes lưu trữ thông tin node server
CREATE TABLE Nodes (
    node_id INT PRIMARY KEY AUTO_INCREMENT,
    ip_address VARCHAR(50) NOT NULL,
    port INT NOT NULL,
    status ENUM('ACTIVE', 'DOWN') DEFAULT 'ACTIVE', -- mặc định là active
    last_heartbeat TIMESTAMP NULL, -- gửi tín hiệu nếu còn active
    current_users INT DEFAULT 0,
    
    UNIQUE KEY unique_node (ip_address, port),
    INDEX idx_status (status)
);

-- Bảng Rooms lưu trữ thông tin phòng
CREATE TABLE Rooms (
    room_id INT PRIMARY KEY AUTO_INCREMENT,
    room_name VARCHAR(100),
    is_private BOOLEAN DEFAULT FALSE,
    password_hash VARCHAR(255), -- nếu phòng ở chế độ private
    owner_id INT,
    node_id INT,
    max_users INT DEFAULT 10,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (owner_id) REFERENCES Users(user_id) ON DELETE SET NULL, -- nếu owner bị xóa, set null
    FOREIGN KEY (node_id) REFERENCES Nodes(node_id) ON DELETE SET NULL,

    INDEX idx_node (node_id)
);

-- Bảng RoomMembers lưu trữ thông tin thành viên có trong phòng
CREATE TABLE RoomMembers (
    id INT PRIMARY KEY AUTO_INCREMENT,
    user_id INT,
    room_id INT,
    is_online BOOLEAN DEFAULT FALSE,
    role ENUM('OWNER', 'MEMBER') DEFAULT 'MEMBER', -- set owner khi là người tạo phòng
    joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (room_id) REFERENCES Rooms(room_id) ON DELETE CASCADE, 
    FOREIGN KEY (user_id) REFERENCES Users(user_id) ON DELETE CASCADE,

    UNIQUE KEY unique_member (user_id, room_id), -- một người chỉ được lưu trữ một lần
    INDEX idx_room (room_id)
);

-- Bảng DrawActions lưu trữ thông tin nét vẽ
CREATE TABLE DrawActions (
    action_id BIGINT PRIMARY KEY AUTO_INCREMENT,
    user_id INT,
    room_id INT,
    type VARCHAR(20),
    data JSON,         
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (room_id) REFERENCES Rooms(room_id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES Users(user_id) ON DELETE SET NULL,

    INDEX idx_room_time (room_id, created_at)
);

-- Bảng Messages lưu trữ tin nhắn
CREATE TABLE Messages (
    message_id BIGINT PRIMARY KEY AUTO_INCREMENT,
    user_id INT,
    room_id INT,
    content TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (room_id) REFERENCES Rooms(room_id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES Users(user_id) ON DELETE SET NULL,

    INDEX idx_room_time_msg (room_id, created_at)
);