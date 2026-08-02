using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/planner")]
[Authorize]
public class PlannerController : ControllerBase
{
    private readonly AppDbContext _context;

    public PlannerController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Planners
            .OrderBy(p => p.Nama)
            .Select(p => new PlannerDto
            {
                IdPlanner = p.IdPlanner,
                Nama = p.Nama,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            }).ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var p = await _context.Planners.FindAsync(id);
        if (p == null) return NotFound();

        return Ok(new PlannerDto
        {
            IdPlanner = p.IdPlanner,
            Nama = p.Nama,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlannerDto dto)
    {
        var entity = new Planner
        {
            Nama = dto.Nama
        };

        _context.Planners.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdatePlannerDto dto)
    {
        var entity = await _context.Planners.FindAsync(id);
        if (entity == null) return NotFound();

        entity.Nama = dto.Nama;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var entity = await _context.Planners.FindAsync(id);
        if (entity == null) return NotFound();

        _context.Planners.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Planner berhasil dihapus" });
    }
}
