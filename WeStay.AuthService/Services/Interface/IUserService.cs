using WeStay.AuthService.Models;

namespace WeStay.AuthService.Services.Interfaces
{
    public interface IUserService
    {
        Task<User> GetUserByIdAsync(int id);
        Task<User> GetUserByEmailAsync(string email);
        Task<User> CreateUserAsync(User user);
        Task<User> UpdateUserAsync(User user);
        Task<bool> CheckPasswordAsync(User user, string password);
        Task<bool> UserExistsAsync(string email);
        Task<bool> UpdateUserStatusAsync(int id,string type);
        Task<User> UpdateUserRoleAsync(int id, UserRole role);
    }
}