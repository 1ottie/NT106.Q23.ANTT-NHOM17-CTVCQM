using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;

[Authorize]
[ApiController]
[Route("api/room")]
public class RoomController : ControllerBase
{
    private readonly RoomService _roomService;

    public RoomController(RoomService roomService)
    {
        _roomService = roomService;
    }

    private int GetUserId()
    {
        var claim = User.FindFirst("user_id");

        if (claim == null)
        {
            Console.WriteLine("[API ROOM] CẢNH BÁO: Không tìm thấy user_id trong Token!");
            throw new Exception("Unauthorized");
        }

        return int.Parse(claim.Value);
    }

    // --- ENDPOINT MỚI CHO NODE SERVER ---
    [AllowAnonymous] // Cho phép Node Server gọi báo cáo trạng thái mà không cần JWT Token
    [HttpPost("update-status")]
    public IActionResult UpdateStatus([FromBody] UserStatusUpdateDto req)
    {
        try
        {
            // Lưu ý: Bạn cần đảm bảo trong RoomService đã có hàm UpdateUserStatus
            _roomService.UpdateUserStatus(req.user_id, req.room_id, req.is_online);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("create")]
    public IActionResult CreateRoom(CreateRoomRequest req)
    {
        Console.WriteLine("\n=========================================");
        try
        {
            int userId = GetUserId();
            Console.WriteLine($"[API ROOM] NHẬN YÊU CẦU TẠO PHÒNG TỪ USER ID: {userId}");
            Console.WriteLine($"[API ROOM] Payload: Tên={req.room_name}, Private={req.is_private}, Có Pass={!string.IsNullOrEmpty(req.password)}, Node={req.node_id}");

            var room = _roomService.CreateRoom(req, userId);

            Console.WriteLine("[API ROOM] => THÀNH CÔNG: Đã tạo phòng và lưu vào Database!");
            Console.WriteLine("=========================================\n");

            return Ok(room);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API ROOM] => LỖI TẠO PHÒNG: {ex.Message}");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("join")]
    public IActionResult JoinRoom(JoinRoomRequest req)
    {
        Console.WriteLine("\n=========================================");
        try
        {
            int userId = GetUserId();
            Console.WriteLine($"[API ROOM] NHẬN YÊU CẦU VÀO PHÒNG TỪ USER ID: {userId}");

            var room = _roomService.JoinRoom(req, userId);

            if (room == null)
            {
                return NotFound(new { message = "Room not found" });
            }

            return Ok(room);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("leave")]
    public IActionResult LeaveRoom(JoinRoomRequest req)
    {
        try
        {
            int userId = GetUserId();
            _roomService.LeaveRoom(req.room_id, userId);
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("list")]
    public IActionResult GetRooms()
    {
        try
        {
            return Ok(_roomService.GetRooms());
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{roomId}/members")]
    public IActionResult GetMembers(int roomId)
    {
        return Ok(_roomService.GetMembers(roomId));
    }
}

// Data Transfer Object cho Node Server
public class UserStatusUpdateDto
{
    public int user_id { get; set; }
    public int room_id { get; set; }
    public int is_online { get; set; } // 1: online, 0: offline
}