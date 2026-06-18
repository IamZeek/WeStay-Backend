using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeStay.BookingService.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBookingReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingReviews");

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 20, 27, 677, DateTimeKind.Utc).AddTicks(6435));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 20, 27, 677, DateTimeKind.Utc).AddTicks(6439));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 20, 27, 677, DateTimeKind.Utc).AddTicks(6440));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 20, 27, 677, DateTimeKind.Utc).AddTicks(6441));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 20, 27, 677, DateTimeKind.Utc).AddTicks(6442));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HostResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingReviews_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2025, 8, 24, 0, 39, 50, 292, DateTimeKind.Utc).AddTicks(8969));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2025, 8, 24, 0, 39, 50, 292, DateTimeKind.Utc).AddTicks(8977));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2025, 8, 24, 0, 39, 50, 292, DateTimeKind.Utc).AddTicks(8980));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2025, 8, 24, 0, 39, 50, 292, DateTimeKind.Utc).AddTicks(8983));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2025, 8, 24, 0, 39, 50, 292, DateTimeKind.Utc).AddTicks(8985));

            migrationBuilder.CreateIndex(
                name: "IX_BookingReviews_BookingId",
                table: "BookingReviews",
                column: "BookingId",
                unique: true);
        }
    }
}
