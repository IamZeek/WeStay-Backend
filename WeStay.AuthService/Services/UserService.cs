using Microsoft.EntityFrameworkCore;
using WeStay.AuthService.Data;
using WeStay.AuthService.Models;
using WeStay.AuthService.Services.Interfaces;

namespace WeStay.AuthService.Services
{
    public class UserService : IUserService
    {
        private readonly AuthDbContext _context;
        private readonly ILogger<UserService> _logger;

        public UserService(AuthDbContext context, ILogger<UserService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<User> GetUserByIdAsync(int id)
        {
            return await _context.Users.FindAsync(id);
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User> CreateUserAsync(User user)
        {
            try
            {
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                return user;
            }
            catch(Exception ex)
            {
                return null;
            }
        }

        public async Task<User> UpdateUserAsync(User user)
        {
            user.UpdatedAt = DateTime.UtcNow;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<bool> CheckPasswordAsync(User user, string password)
        {
            return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        }

        public async Task<bool> UserExistsAsync(string email)
        {
            return await _context.Users.AnyAsync(u => u.Email == email);
        }

        public async Task<User> UpdateUserRoleAsync(int id, UserRole role)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                throw new KeyNotFoundException("User not found");
            }

            user.Role = role;
            user.UpdatedAt = DateTime.UtcNow;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated role for user {UserId} to {Role}", id, role);
            return user;
        }

        public async Task<bool> UpdateUserStatusAsync(int id,string type)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (user != null)
                return false;
            user.UpdatedAt = DateTime.UtcNow;
            if(type == "Email")
                user.IsEmailVerified = true;
            if (type == "Phone")
                user.IsPhoneNoVerified = true;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            return true;

        }
    }
}