using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using WeStay.AuthService.Models;
using WeStay.AuthService.Models.Requests;
using WeStay.AuthService.Services;
using WeStay.AuthService.Services.Interfaces;
using WeStay.AuthService.Utilities;
using WeStay.AuthService.Utilities;

namespace WeStay.AuthService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IExternalAuthService _externalAuthService;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IAuthService _authService;
        private readonly IUserService _userService;
        private readonly IVerificationService _verificationService;
        private readonly IPhoneVerificationService _phoneVerificationService;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _cache;

        public AuthController(
            IConfiguration configuration,
            ILogger<AuthController> logger,
            IExternalAuthService externalAuthService,
            IJwtTokenGenerator jwtTokenGenerator,
            IAuthService authService,
            IUserService userService,
            IVerificationService verificationService,
            IPhoneVerificationService phoneVerificationService, 
            IEmailService emailService,
            IMemoryCache cache)
        {
            _configuration = configuration;
            _logger = logger;
            _externalAuthService = externalAuthService;
            _jwtTokenGenerator = jwtTokenGenerator;
            _authService = authService;
            _userService = userService;
            _verificationService = verificationService;
            _phoneVerificationService = phoneVerificationService;
            _emailService = emailService;
            _cache = cache;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                // Validate model
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid registration data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                // Register user
                var user = await _authService.RegisterUserAsync(
                    request.Email,
                    request.Password,
                    request.FirstName,
                    request.LastName,
                    request.DateOfBirth,
                    request.PhoneNumber);

                // Generate JWT token
                var token = _jwtTokenGenerator.GenerateToken(user);

                // Return success response
                return Ok(new
                {
                    Token = token,
                    User = new
                    {
                        user.Id,
                        user.Email,
                        user.FirstName,
                        user.LastName,
                        user.DateOfBirth,
                        user.PhoneNumber
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration for email: {Email}", request.Email);
                return StatusCode(500, new { Message = "An error occurred during registration" });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                // Validate model
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid login data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                // Login user
                var user = await _authService.LoginUserAsync(request.Email, request.Password);

                // Generate JWT token
                var token = _jwtTokenGenerator.GenerateToken(user);

                // Return success response
                return Ok(new
                {
                    Token = token,
                    User = new
                    {
                        user.Id,
                        user.Email,
                        user.FirstName,
                        user.LastName,
                        user.ProfilePicture,
                        user.DateOfBirth,
                        user.PhoneNumber
                    }
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user login for email: {Email}", request.Email);
                return StatusCode(500, new { Message = "An error occurred during login" });
            }
        }
        
        [HttpGet("external-login")]
        public async Task<IActionResult> ExternalLogin(
            [FromServices] IAuthenticationSchemeProvider schemeProvider,
            [FromQuery] string provider,
            [FromQuery] string returnUrl = null)
        {
            try
            {
                // Google is the only supported external provider.
                if (string.IsNullOrWhiteSpace(provider) || !provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { Message = "Unsupported authentication provider. Supported providers: Google" });
                }

                // The provider's auth scheme is only registered when its credentials are configured.
                // If it isn't, return a clean 400 instead of a 500 from Challenge().
                var scheme = await schemeProvider.GetSchemeAsync("Google");
                if (scheme == null)
                {
                    return BadRequest(new { Message = "Google login is not configured on the server." });
                }

                // Create redirect URL for callback
                var redirectUrl = Url.Action("ExternalLoginCallback", "Auth", new { returnUrl });
                var properties = new AuthenticationProperties
                {
                    RedirectUri = redirectUrl,
                    Items = { { "LoginProvider", "Google" } }
                };

                // Challenge the external provider
                return Challenge(properties, "Google");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating external login with provider: {Provider}", provider);
                return StatusCode(500, new { Message = "An error occurred during external login initiation" });
            }
        }

        [HttpGet("external-login-callback")]
        public async Task<IActionResult> ExternalLoginCallback([FromQuery] string returnUrl = null)
        {
            try
            {
                // Authenticate the user with the external provider
                var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                if (!authResult.Succeeded)
                {
                    _logger.LogWarning("External authentication failed: {Failure}", authResult.Failure?.Message);
                    return BadRequest(new { Message = "External authentication failed" });
                }

                // Get the provider from authentication properties
                var provider = authResult.Properties.Items["LoginProvider"];
                if (string.IsNullOrEmpty(provider))
                {
                    return BadRequest(new { Message = "Authentication provider not specified" });
                }

                // Handle the external login
                var user = await _externalAuthService.HandleExternalLoginAsync(provider, authResult.Principal.Claims);
                if (user == null)
                {
                    return BadRequest(new { Message = "Failed to process external authentication" });
                }

                // Generate JWT token
                var token = _jwtTokenGenerator.GenerateToken(user);

                // Clean up external cookie
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                // For API clients, return JSON with token
                if (IsApiRequest())
                {
                    return Ok(new
                    {
                        Token = token,
                        User = new
                        {
                            user.Id,
                            user.Email,
                            user.FirstName,
                            user.LastName,
                            user.ProfilePicture
                        },
                        ReturnUrl = returnUrl
                    });
                }

                // For web clients, redirect with token
                var frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
                var redirectUrl = $"{frontendUrl}/auth/callback?token={Uri.EscapeDataString(token)}&userId={user.Id}";

                if (!string.IsNullOrEmpty(returnUrl))
                {
                    redirectUrl += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                }

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during external login callback");
                return StatusCode(500, new { Message = "An error occurred during external login processing" });
            }
        }

        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                // Get user ID from claims
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                var user = await _userService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    return NotFound(new { Message = "User not found" });
                }

                // Return user profile
                return Ok(new
                {
                    user.Id,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.ProfilePicture,
                    user.DateOfBirth,
                    user.PhoneNumber,
                    user.CreatedAt,
                    user.ExternalType,
                    user.IsPhoneNoVerified,
                    user.IsEmailVerified
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user profile for user ID: {UserId}",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { Message = "An error occurred while retrieving profile" });
            }
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                // Validate model
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid password change data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                // Get user ID from claims
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

                // Change password
                var user = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);

                return Ok(new { Message = "Password changed successfully" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for user ID: {UserId}",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { Message = "An error occurred while changing password" });
            }
        }

        /// <summary>
        /// Promote the currently authenticated user to Host (self-service).
        /// Returns a fresh JWT carrying the new Host role so the client doesn't need to re-login.
        /// </summary>
        [HttpPost("become-host")]
        [Authorize]
        public async Task<IActionResult> BecomeHost()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { Message = "User not found" });
                }

                if (user.Role == UserRole.Admin)
                {
                    return BadRequest(new { Message = "Admins cannot change their own role to Host." });
                }

                if (user.Role != UserRole.Host)
                {
                    user = await _userService.UpdateUserRoleAsync(userId, UserRole.Host);
                }

                // Re-issue the token so the Host role claim is available immediately.
                var token = _jwtTokenGenerator.GenerateToken(user);
                return Ok(new { Message = "You are now a Host.", Token = token, Role = user.Role.ToString() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error promoting user to Host");
                return StatusCode(500, new { Message = "An error occurred while updating your role" });
            }
        }

        /// <summary>
        /// Admin-only: set any user's role (Guest/Host/Admin). The target user must re-login
        /// (or be re-issued a token) for their new role claim to take effect.
        /// </summary>
        [HttpPut("users/{id}/role")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetUserRole(int id, [FromBody] SetUserRoleRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid role data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                var user = await _userService.UpdateUserRoleAsync(id, request.Role);
                return Ok(new { Message = $"User {id} role set to {user.Role}.", user.Id, Role = user.Role.ToString() });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting role for user {UserId}", id);
                return StatusCode(500, new { Message = "An error occurred while setting the user role" });
            }
        }

        // Add this method to the AuthController class
        [HttpPut("verification-update")]
        [Authorize]
        public async Task<IActionResult> VerificationUpdate([FromBody] Verification request)
        {
            try
            {
                // Validate model
                if (!ModelState.IsValid)
                {
                    return BadRequest(new { Message = "Invalid verification data", Errors = ModelState.Values.SelectMany(v => v.Errors) });
                }

                // Get current user ID
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                var user = await _userService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    return NotFound(new { Message = "User not found" });
                }

                // Get user's existing verification
                var verification = await _verificationService.GetVerificationByUserIdAsync(userId);

                if (verification == null)
                {
                    // Create new verification if it doesn't exist
                    verification = new Verification
                    {
                        UserId = userId,
                        DocumentType = request.DocumentType,
                        DocumentNumber = request.DocumentNumber,
                        ImageUrl = request.ImageUrl,
                        Status = VerificationStatus.Pending, // Reset to pending when updating
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await _verificationService.CreateVerificationAsync(verification);

                    _logger.LogInformation("Created verification record for user {UserId}", userId);
                }
                else
                {
                    // Update existing verification
                    verification.DocumentType = request.DocumentType;
                    verification.DocumentNumber = request.DocumentNumber;
                    verification.ImageUrl = request.ImageUrl;
                    verification.Status = VerificationStatus.Pending; // Reset to pending when updating
                    verification.UpdatedAt = DateTime.UtcNow;
                    verification.VerifiedAt = null; // Reset verification timestamp
                    verification.RejectionReason = null; // Clear previous rejection reason

                    await _verificationService.UpdateVerificationAsync(verification);

                    _logger.LogInformation("Updated verification record for user {UserId}", userId);
                }

                return Ok(new
                {
                    Message = "Verification information updated successfully",
                    Verification = new
                    {
                        verification.Id,
                        verification.DocumentType,
                        verification.DocumentNumber,
                        verification.ImageUrl,
                        verification.Status,
                        verification.UpdatedAt
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating verification for user ID: {UserId}",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { Message = "An error occurred while updating verification information" });
            }
        }

        [HttpPost("send-otp-phone")]
        [Authorize]
        public async Task<IActionResult> SendOtpPhone([FromBody] string phoneNumber)
        {
            await _phoneVerificationService.SendOtpAsync(phoneNumber);
            return Ok(new { Message = "OTP sent successfully" });
        }

        [HttpPost("verify-otp-phone")]
        [Authorize]
        public async Task<IActionResult> VerifyOtpPhone([FromBody] dynamic request,int userId)
        {
            string phoneNumber = request.phoneNumber;
            string otp = request.otp;

            var isValid = await _phoneVerificationService.VerifyOtp(phoneNumber, otp, userId);
            if (!isValid)
                return BadRequest(new { Message = "Invalid OTP" });
            
            return Ok(new { Message = "Phone verified successfully" });
        }

        [HttpPost("send-otp-email")]
        [Authorize]
        public async Task<IActionResult> SendOtpEmail()
        {
            var otp = new Random().Next(100000, 999999).ToString();
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            _cache.Set(email, otp, TimeSpan.FromMinutes(5)); // store OTP for 5 minutes
            await _emailService.SendOtpAsync(email, otp);
            return Ok(new { message = "OTP sent to email." });
        }

        [HttpPost("verify-otp-email")]
        [Authorize]
        public IActionResult VerifyOtpEmail(string code)
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            if (_cache.TryGetValue(email, out string otp) && otp == code)
            {
                _cache.Remove(email);
                return Ok(new { message = "OTP verified successfully." });
            }
            return BadRequest(new { message = "Invalid or expired OTP." });
        }


        #region Helper Methods

        private bool IsApiRequest()
        {
            return Request.Headers.Accept.Any(accept => accept.Contains("application/json"));
        }

        #endregion
    }
}