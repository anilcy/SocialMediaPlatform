using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using SocialMediaPlatform.Business.Interfaces;
using SocialMediaPlatform.Data.Interfaces;
using SocialMediaPlatform.Entities.Dtos;
using SocialMediaPlatform.Entities.Models;
using Microsoft.AspNetCore.Identity;
using SocialMediaPlatform.Data;
using SocialMediaPlatform.Entities.Dtos.UserDtos;

namespace SocialMediaPlatform.Business.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IFollowRepository _followRepository;
    private readonly IMapper _mapper;
    private readonly UserManager<AppUser> _userManager;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorageService;

    public UserService(IUserRepository userRepository, IFollowRepository followRepository,
                      IMapper mapper, UserManager<AppUser> userManager, IUnitOfWork unitOfWork,
                      IFileStorageService fileStorageService)
    {
        _userRepository = userRepository;
        _followRepository = followRepository;
        _mapper = mapper;
        _userManager = userManager;
        _unitOfWork = unitOfWork;
        _fileStorageService = fileStorageService;
    }

    public async Task<UserDto> GetUserProfileAsync(Guid userId, Guid? currentUserId = null)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found");

        var userDto = _mapper.Map<UserDto>(user);
        
        // Get statistics
        userDto.PostsCount = await _userRepository.GetUserPostsCountAsync(userId);
        userDto.FollowersCount = await _followRepository.GetFollowersCountAsync(userId);
        userDto.FollowingCount = await _followRepository.GetFollowingCountAsync(userId);

        // If current user is provided, check relationship
        if (currentUserId.HasValue && currentUserId != userId)
        {
            var followRelation = await _followRepository.GetFollowRelationshipAsync(currentUserId.Value, userId);
            userDto.FollowStatus = followRelation?.Status;
        }

        return userDto;
    }

    public async Task<UserDto> GetUserByUserNameAsync(string userName, Guid? currentUserId = null)
    {
        var user = await _userRepository.GetUserByUserNameAsync(userName);
        if (user == null)
            throw new KeyNotFoundException("User not found");

        return await GetUserProfileAsync(user.Id, currentUserId);
    }

    public async Task<UserDto> UpdateUserProfileAsync(Guid userId, UserProfileUpdateDto dto)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found");
        
        // Keep the current picture unless a new one is uploaded
        var pictureUrl = user.ProfilePictureUrl;
        if (dto.ProfilePicture != null)
            pictureUrl = await _fileStorageService.UploadFileAsync(dto.ProfilePicture);

        user.UpdateProfile(dto.FullName, pictureUrl, dto.Bio, dto.WebsiteUrl);
        await _unitOfWork.SaveChangesAsync();
        
        return await GetUserProfileAsync(userId);
    }

    public async Task<List<UserSummaryDto>> SearchUsersAsync(string searchTerm, int page = 1, int pageSize = 20)
    {
        var users = await _userRepository.SearchUsersAsync(searchTerm, page, pageSize);
        return _mapper.Map<List<UserSummaryDto>>(users);
    }

    public async Task UpdateLastLoginAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found");

        user.UpdateLastLoginDate();
        await _unitOfWork.SaveChangesAsync();
    }
    
    public async Task SetPrivacyAsync(Guid userId, bool isPrivate)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found");

        user.SetPrivate(isPrivate);
        await _unitOfWork.SaveChangesAsync();
    }
    
    public async Task DeactivateAccountAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found");

        user.DeactivateAccount();
        await _unitOfWork.SaveChangesAsync();
    }
    
    public async Task SoftDeleteAccountAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User not found");

        user.SoftDeleteAccount();
        await _unitOfWork.SaveChangesAsync();
    }
}