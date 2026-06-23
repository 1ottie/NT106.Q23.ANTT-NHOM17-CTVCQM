using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;

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
            _roomService.UpdateUserStatus(req.user_id, req.room_id, req.is_online);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[MASTER - ROOM] Cập nhật trạng thái DB: User ID {req.user_id} -> Phòng {req.room_id} -> {(req.is_online == 1 ? "ONLINE" : "OFFLINE")}");
            Console.ResetColor();
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
        try
        {
            int userId = GetUserId();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"[MASTER - ROOM] User ID {userId} yêu cầu tạo phòng vẽ mới: '{req.room_name}'");
            Console.ResetColor();
            var result = _roomService.CreateRoom(req, userId);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[MASTER SERVER - LOAD BALANCER] Tạo phòng thành công. Least connection chọn Node [{result.nodeIp}:{result.nodePort}] ({result.currentUsers} users online).");
            Console.ResetColor();

            return Ok(result);
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("join")]
    public IActionResult JoinRoom(JoinRoomRequest req)
    {
        try
        {
            int userId = GetUserId();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"[MASTER - ROOM] User ID {userId} gửi yêu cầu xin tham gia vào phòng ID: {req.room_id}");
            Console.ResetColor();

            var result = _roomService.JoinRoom(req, userId);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[MASTER SERVER - LOAD BALANCER] ĐIỀU HƯỚNG THÀNH CÔNG: User {userId} -> Node [{result.nodeIp}:{result.nodePort}] ({result.currentUsers} users online).");
            Console.ResetColor();

            return Ok(result);
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("leave")]
    public IActionResult LeaveRoom(JoinRoomRequest req)
    {
        try
        {
            int userId = GetUserId();
            _roomService.LeaveRoom(req.room_id, userId);
            
            Console.ForegroundColor = ConsoleColor.Magenta; 
            Console.WriteLine($"[MASTER - ROOM] User ID {userId} đã ngắt kết nối và rời phòng {req.room_id}.");
            Console.ResetColor();
             
            return Ok();
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
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

public class UserStatusUpdateDto
{
    public int user_id { get; set; }
    public int room_id { get; set; }
    public int is_online { get; set; } // 1: online, 0: offline
}
