using SocialMediaPlatform.Business.Interfaces;
using SocialMediaPlatform.Entities.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using SocialMediaPlatform.Entities.Dtos.UserDtos;

namespace SocialMediaPlatform.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : BaseController
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [AllowAnonymous]
    [HttpGet("{userId}")]
    public async Task<IActionResult> GetUserProfile(Guid userId)
    {
        var user = await _userService.GetUserProfileAsync(userId, CurrentUserIdOrNull);
        return Ok(user);
    }
    
    [AllowAnonymous]
    [HttpGet("username/{userName}")]
    public async Task<IActionResult> GetUserByUserName(string userName)
    {
        var user = await _userService.GetUserByUserNameAsync(userName, CurrentUserIdOrNull);
        return Ok(user);
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var user = await _userService.GetUserProfileAsync(CurrentUserId);
        return Ok(user);
    }

    [HttpPut("me/update-profile")]
    public async Task<IActionResult> UpdateMyProfile([FromForm] UserProfileUpdateDto userProfileUpdateDto)
    {
        var user = await _userService.UpdateUserProfileAsync(CurrentUserId, userProfileUpdateDto);
        return Ok(user);
    }

    [AllowAnonymous]
    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string searchTerm, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var users = await _userService.SearchUsersAsync(searchTerm, page, pageSize);
        return Ok(users);
    }
    
    [HttpPut("me/privacy")]
    public async Task<IActionResult> SetPrivacy([FromBody] bool isPrivate)
    {
        await _userService.SetPrivacyAsync(CurrentUserId, isPrivate);
        return NoContent();
    }
    

    [HttpPost("me/deactivate")]
    public async Task<IActionResult> DeactivateAccount()
    {
        await _userService.DeactivateAccountAsync(CurrentUserId);
        return NoContent();
    }

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteAccount()
    {
        await _userService.SoftDeleteAccountAsync(CurrentUserId);
        return NoContent();
    }
    
}