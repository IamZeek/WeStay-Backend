using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WeStay.BookingService.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformFeeConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuestServiceFee = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    HostPlatformFee = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    CancellationFeePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformFeeConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookingCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ListingId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CheckInDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CheckOutDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NumberOfGuests = table.Column<int>(type: "integer", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    BasePrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    GuestServiceFeeAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    GuestTotalPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    HostPlatformFeeAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    HostPayoutAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    StatusId = table.Column<int>(type: "integer", nullable: false),
                    SpecialRequests = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    BookingStatusId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_BookingStatuses_BookingStatusId",
                        column: x => x.BookingStatusId,
                        principalTable: "BookingStatuses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Bookings_BookingStatuses_StatusId",
                        column: x => x.StatusId,
                        principalTable: "BookingStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingGuests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookingId = table.Column<int>(type: "integer", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingGuests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingGuests_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookingId = table.Column<int>(type: "integer", nullable: false),
                    PaymentIntentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingPayments_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookingId = table.Column<int>(type: "integer", nullable: false),
                    Tracker = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    HostPayoutAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    CancellationFeeAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    SafepayRefundRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Releasable = table.Column<bool>(type: "boolean", nullable: false),
                    PaidOut = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    HeldAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReleasableAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PaidOutAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "BookingStatuses",
                columns: new[] { "Id", "CreatedAt", "Description", "Name" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 9, 13, 1, 57, 50, 66, DateTimeKind.Utc).AddTicks(4102), "Booking is created but not confirmed", "Pending" },
                    { 2, new DateTime(2026, 9, 13, 1, 57, 50, 66, DateTimeKind.Utc).AddTicks(4107), "Booking is confirmed and active", "Confirmed" },
                    { 3, new DateTime(2026, 9, 13, 1, 57, 50, 66, DateTimeKind.Utc).AddTicks(4109), "Booking has been cancelled", "Cancelled" },
                    { 4, new DateTime(2026, 9, 13, 1, 57, 50, 66, DateTimeKind.Utc).AddTicks(4110), "Booking has been completed successfully", "Completed" },
                    { 5, new DateTime(2026, 9, 13, 1, 57, 50, 66, DateTimeKind.Utc).AddTicks(4111), "Booking was cancelled and refunded", "Refunded" },
                    { 6, new DateTime(2026, 9, 13, 1, 57, 50, 66, DateTimeKind.Utc).AddTicks(4113), "Booking was rejected by the host", "Rejected" }
                });

            migrationBuilder.InsertData(
                table: "PlatformFeeConfigs",
                columns: new[] { "Id", "CancellationFeePercent", "GuestServiceFee", "HostPlatformFee", "UpdatedAt" },
                values: new object[] { 1, 10m, 8m, 2m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_BookingGuests_BookingId",
                table: "BookingGuests",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPayments_BookingId",
                table: "BookingPayments",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPayments_PaymentIntentId",
                table: "BookingPayments",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingCode",
                table: "Bookings",
                column: "BookingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingStatusId",
                table: "Bookings",
                column: "BookingStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CheckInDate",
                table: "Bookings",
                column: "CheckInDate");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CheckOutDate",
                table: "Bookings",
                column: "CheckOutDate");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ListingId",
                table: "Bookings",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_StatusId",
                table: "Bookings",
                column: "StatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_UserId",
                table: "Bookings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BookingId",
                table: "Payments",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Tracker",
                table: "Payments",
                column: "Tracker");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingGuests");

            migrationBuilder.DropTable(
                name: "BookingPayments");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "PlatformFeeConfigs");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "BookingStatuses");
        }
    }
}
