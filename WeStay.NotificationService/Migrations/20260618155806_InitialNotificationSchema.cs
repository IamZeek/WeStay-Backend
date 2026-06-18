using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WeStay.NotificationService.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotificationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SubjectTemplate = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BodyTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Variables = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TemplateSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TemplateBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SMSEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PushEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MarketingEmails = table.Column<bool>(type: "bit", nullable: false),
                    BookingNotifications = table.Column<bool>(type: "bit", nullable: false),
                    SecurityNotifications = table.Column<bool>(type: "bit", nullable: false),
                    Newsletter = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TypeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    IsSent = table.Column<bool>(type: "bit", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_NotificationTypes_TypeId",
                        column: x => x.TypeId,
                        principalTable: "NotificationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "BodyTemplate", "Channel", "CreatedAt", "Description", "IsActive", "Language", "Name", "SubjectTemplate", "UpdatedAt", "Variables" },
                values: new object[,]
                {
                    { 1, "<h1>Booking Confirmed</h1><p>Dear {{UserName}},</p><p>Your booking {{BookingCode}} has been confirmed.</p><p>Check-in: {{CheckInDate}}</p><p>Check-out: {{CheckOutDate}}</p>", "Email", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(384), "Booking confirmation email", true, "en", "BookingConfirmation_Email", "Booking Confirmed - {{BookingCode}}", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(385), "[\"UserName\", \"BookingCode\", \"CheckInDate\", \"CheckOutDate\"]" },
                    { 2, "Your booking {{BookingCode}} is confirmed. Check-in: {{CheckInDate}}", "SMS", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(387), "Booking confirmation SMS", true, "en", "BookingConfirmation_SMS", "Booking Confirmed", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(387), "[\"BookingCode\", \"CheckInDate\"]" },
                    { 3, "<h1>Welcome {{UserName}}!</h1><p>Thank you for joining WeStay.</p>", "Email", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(389), "Welcome email", true, "en", "Welcome_Email", "Welcome to WeStay!", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(389), "[\"UserName\"]" },
                    { 4, "<p>Click here to reset your password: <a href=\"{{ResetLink}}\">Reset Password</a></p>", "Email", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(391), "Password reset email", true, "en", "PasswordReset_Email", "Reset Your Password", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(391), "[\"ResetLink\"]" }
                });

            migrationBuilder.InsertData(
                table: "NotificationTypes",
                columns: new[] { "Id", "CreatedAt", "Description", "IsActive", "Name", "TemplateBody", "TemplateSubject", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(279), "Booking confirmation notification", true, "BookingConfirmation", "Dear {{UserName}}, your booking {{BookingCode}} has been confirmed.", "Booking Confirmation - {{BookingCode}}", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(279) },
                    { 2, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(281), "Booking cancellation notification", true, "BookingCancellation", "Dear {{UserName}}, your booking {{BookingCode}} has been cancelled.", "Booking Cancelled - {{BookingCode}}", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(282) },
                    { 3, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(284), "Payment confirmation notification", true, "PaymentReceived", "Dear {{UserName}}, payment for booking {{BookingCode}} has been received.", "Payment Received - {{BookingCode}}", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(284) },
                    { 4, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(286), "Payment failure notification", true, "PaymentFailed", "Dear {{UserName}}, payment for booking {{BookingCode}} has failed.", "Payment Failed - {{BookingCode}}", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(286) },
                    { 5, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(288), "Review reminder notification", true, "ReviewReminder", "Dear {{UserName}}, how was your stay? Please leave a review.", "Review Your Stay - {{BookingCode}}", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(289) },
                    { 6, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(291), "Security alert notification", true, "SecurityAlert", "Security alert: {{Message}}", "Security Alert - {{Subject}}", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(291) },
                    { 7, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(293), "Password reset notification", true, "PasswordReset", "Click here to reset your password: {{ResetLink}}", "Password Reset Request", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(293) },
                    { 8, new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(295), "Welcome notification", true, "Welcome", "Dear {{UserName}}, welcome to WeStay! Start exploring properties.", "Welcome to WeStay!", new DateTime(2026, 6, 18, 15, 58, 5, 677, DateTimeKind.Utc).AddTicks(295) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Channel",
                table: "Notifications",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsSent",
                table: "Notifications",
                column: "IsSent");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TypeId",
                table: "Notifications",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_Channel",
                table: "NotificationTemplates",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_Name",
                table: "NotificationTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationPreferences_UserId",
                table: "UserNotificationPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "UserNotificationPreferences");

            migrationBuilder.DropTable(
                name: "NotificationTypes");
        }
    }
}
