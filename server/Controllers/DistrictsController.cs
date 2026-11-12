using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Route("api/districts")]
public sealed class DistrictsController : ControllerBase
{
    private readonly DistrictService _districtService;

    public DistrictsController(DistrictService districtService)
    {
        _districtService = districtService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDistricts()
    {
        return Ok(await _districtService.GetAllAsync());
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateDistrict(DistrictRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "District name is required." });
        }

        if (!DistrictMemberCode.TryGet(request.Name, out _))
            return BadRequest(new { message = "No member ID code is configured for this district." });

        if (string.IsNullOrWhiteSpace(request.WhatsAppGroupLink))
        {
            return BadRequest(new { message = "WhatsApp group link is required." });
        }

        var district = await _districtService.CreateAsync(new District
        {
            Name = request.Name.Trim(),
            WhatsAppGroupLink = request.WhatsAppGroupLink.Trim()
        });

        return CreatedAtAction(nameof(GetDistricts), new { id = district.Id }, district);
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDistrict(string id, DistrictRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "District name is required." });
        }

        if (!DistrictMemberCode.TryGet(request.Name, out _))
            return BadRequest(new { message = "No member ID code is configured for this district." });

        if (string.IsNullOrWhiteSpace(request.WhatsAppGroupLink))
        {
            return BadRequest(new { message = "WhatsApp group link is required." });
        }

        var updated = await _districtService.UpdateAsync(id, request.Name.Trim(), request.WhatsAppGroupLink.Trim());
        return updated ? NoContent() : NotFound(new { message = "District not found." });
    }
}
