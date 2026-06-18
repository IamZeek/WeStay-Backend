using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeStay.BookingService.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectedBookingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 48, 16, 82, DateTimeKind.Utc).AddTicks(946));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 48, 16, 82, DateTimeKind.Utc).AddTicks(1047));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 48, 16, 82, DateTimeKind.Utc).AddTicks(1051));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 48, 16, 82, DateTimeKind.Utc).AddTicks(1052));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 48, 16, 82, DateTimeKind.Utc).AddTicks(1052));

            migrationBuilder.InsertData(
                table: "BookingStatuses",
                columns: new[] { "Id", "CreatedAt", "Description", "Name" },
                values: new object[] { 6, new DateTime(2026, 6, 18, 17, 48, 16, 82, DateTimeKind.Utc).AddTicks(1053), "Booking was rejected by the host", "Rejected" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 6);

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
    }
}
