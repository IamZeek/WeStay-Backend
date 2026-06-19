using WeStay.AuthService.Data;
using WeStay.AuthService.Models;
using WeStay.AuthService.Services.Interfaces;

using Microsoft.EntityFrameworkCore;
using WeStay.AuthService.Data;
using WeStay.AuthService.Models;
using WeStay.AuthService.Services.Interfaces;

namespace WeStay.AuthService.Services
{
    public class VerificationService : IVerificationService
    {
        private readonly AuthDbContext _context;
        private readonly ILogger<VerificationService> _logger;

        public VerificationService(AuthDbContext context, ILogger<VerificationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Verification> GetVerificationByUserIdAsync(int userId)
        {
            return await _context.Verifications
                .FirstOrDefaultAsync(v => v.UserId == userId);
        }

        public async Task<Verification> GetVerificationByIdAsync(int id)
        {
            return await _context.Verifications
                .Include(v => v.User)
                .FirstOrDefaultAsync(v => v.Id == id);
        }

        public async Task<Verification> CreateVerificationAsync(Verification verification)
        {
            // Check if user already has a verification
            var existingVerification = await GetVerificationByUserIdAsync(verification.UserId);
            if (existingVerification != null)
            {
                throw new InvalidOperationException("User already has a verification record");
            }

            verification.CreatedAt = DateTime.UtcNow;
            verification.UpdatedAt = DateTime.UtcNow;

            _context.Verifications.Add(verification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created verification record {VerificationId} for user {UserId}",
                verification.Id, verification.UserId);

            return verification;
        }

        public async Task<Verification> UpdateVerificationAsync(Verification verification)
        {
            verification.UpdatedAt = DateTime.UtcNow;
            _context.Verifications.Update(verification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated verification record {VerificationId}", verification.Id);

            return verification;
        }
        // Add this method to the VerificationService class
        public async Task<Verification> UpdateVerificationStatusAsync(int verificationId, VerificationStatus status, string rejectionReason = null)
        {
            var verification = await GetVerificationByIdAsync(verificationId);
            if (verification == null)
            {
                throw new KeyNotFoundException("Verification not found");
            }

            verification.Status = status;
            verification.UpdatedAt = DateTime.UtcNow;

            if (status == VerificationStatus.Approved)
            {
                verification.VerifiedAt = DateTime.UtcNow;
                verification.RejectionReason = string.Empty; // NOT NULL column
            }
            else if (status == VerificationStatus.Rejected)
            {
                verification.VerifiedAt = DateTime.UtcNow;
                verification.RejectionReason = rejectionReason ?? string.Empty;
            }
            else
            {
                verification.VerifiedAt = null;
                verification.RejectionReason = string.Empty; // NOT NULL column
            }

            _context.Verifications.Update(verification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated verification status to {Status} for verification {VerificationId}",
                status, verificationId);

            return verification;
        }
        public async Task<bool> DeleteVerificationAsync(int id)
        {
            var verification = await GetVerificationByIdAsync(id);
            if (verification == null)
            {
                return false;
            }

            _context.Verifications.Remove(verification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted verification record {VerificationId}", id);

            return true;
        }

        public async Task<IEnumerable<Verification>> GetVerificationsByStatusAsync(VerificationStatus status)
        {
            return await _context.Verifications
                .Include(v => v.User)
                .Where(v => v.Status == status)
                .ToListAsync();
        }

        public async Task<bool> UserHasVerificationAsync(int userId)
        {
            return await _context.Verifications
                .AnyAsync(v => v.UserId == userId);
        }
    }
}