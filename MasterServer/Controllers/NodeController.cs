using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/node")]
public class NodeController : ControllerBase
{
    private readonly NodeService _nodeService;

    public NodeController(NodeService nodeService)
    {
        _nodeService = nodeService;
    }

    [HttpPost("register")]
    public IActionResult Register(RegisterNodeRequest req)
    {
        var nodeId = _nodeService.RegisterNode(req);
        return Ok(new { node_id = nodeId });
    }

    [HttpPost("heartbeat")]
    public IActionResult Heartbeat(HeartbeatRequest req)
    {
        var ok = _nodeService.UpdateHeartbeat(req.node_id);

        if (!ok)
            return NotFound(new { message = "Node not found" });

        return Ok();
    }

    [HttpGet("list")]
    public IActionResult GetNodes()
    {
        return Ok(_nodeService.GetAllNodes());
    }
}