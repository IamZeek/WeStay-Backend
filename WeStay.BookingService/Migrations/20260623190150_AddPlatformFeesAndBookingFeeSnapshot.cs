using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeStay.BookingService.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformFeesAndBookingFeeSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BasePrice",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GuestServiceFeeAmount",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GuestTotalPrice",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HostPayoutAmount",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HostPlatformFeeAmount",
                table: "Bookings",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            // Backfill bookings created before fees existed with 0% fees applied (don't rewrite
            // history): BasePrice = the original TotalPrice, GuestTotalPrice = BasePrice, HostPayout
            // = BasePrice. The two fee-amount columns keep their 0 default.
            migrationBuilder.Sql(
                "UPDATE [Bookings] SET [BasePrice] = [TotalPrice], [GuestTotalPrice] = [TotalPrice], [HostPayoutAmount] = [TotalPrice];");

            migrationBuilder.CreateTable(
                name: "PlatformFeeConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GuestServiceFee = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    HostPlatformFee = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformFeeConfigs", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 23, 19, 1, 50, 284, DateTimeKind.Utc).AddTicks(9962));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 23, 19, 1, 50, 284, DateTimeKind.Utc).AddTicks(9967));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 23, 19, 1, 50, 284, DateTimeKind.Utc).AddTicks(9968));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 23, 19, 1, 50, 284, DateTimeKind.Utc).AddTicks(9970));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 23, 19, 1, 50, 284, DateTimeKind.Utc).AddTicks(9971));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 6,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 23, 19, 1, 50, 284, DateTimeKind.Utc).AddTicks(9973));

            migrationBuilder.InsertData(
                table: "PlatformFeeConfigs",
                columns: new[] { "Id", "GuestServiceFee", "HostPlatformFee", "UpdatedAt" },
                values: new object[] { 1, 8m, 2m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformFeeConfigs");

            migrationBuilder.DropColumn(
                name: "BasePrice",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "GuestServiceFeeAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "GuestTotalPrice",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "HostPayoutAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "HostPlatformFeeAmount",
                table: "Bookings");

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

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 6,
                column: "CreatedAt",
                value: new DateTime(2026, 6, 18, 17, 48, 16, 82, DateTimeKind.Utc).AddTicks(1053));
        }
    }
}
