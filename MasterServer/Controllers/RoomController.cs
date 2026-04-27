using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

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
            throw new Exception("Unauthorized");

        return int.Parse(claim.Value);
    }

    [HttpPost("create")]
    public IActionResult CreateRoom(CreateRoomRequest req)
    {
        try
        {
            var room = _roomService.CreateRoom(req, GetUserId());
            return Ok(room);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("join")]
    public IActionResult JoinRoom(JoinRoomRequest req)
    {
        try
        {
            var room = _roomService.JoinRoom(req, GetUserId());

            if (room == null)
                return NotFound(new { message = "Room not found" });

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
        _roomService.LeaveRoom(req.room_id, GetUserId());
        return Ok();
    }

    [HttpGet("list")]
    public IActionResult GetRooms()
    {
        return Ok(_roomService.GetRooms());
    }

    [HttpGet("{roomId}/members")]
    public IActionResult GetMembers(int roomId)
    {
        return Ok(_roomService.GetMembers(roomId));
    }
}