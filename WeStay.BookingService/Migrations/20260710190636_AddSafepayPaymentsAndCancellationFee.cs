using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeStay.BookingService.Migrations
{
    /// <inheritdoc />
    public partial class AddSafepayPaymentsAndCancellationFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CancellationFeePercent",
                table: "PlatformFeeConfigs",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            // WeStay is PKR end-to-end; the old "USD" default was a mislabel. Correct existing rows.
            migrationBuilder.Sql("UPDATE [Bookings] SET [Currency] = 'PKR' WHERE [Currency] = 'USD';");

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    Tracker = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    HostPayoutAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    CancellationFeeAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    SafepayRefundRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Releasable = table.Column<bool>(type: "bit", nullable: false),
                    PaidOut = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HeldAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasableAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidOutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 10, 19, 6, 35, 811, DateTimeKind.Utc).AddTicks(1223));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 2,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 10, 19, 6, 35, 811, DateTimeKind.Utc).AddTicks(1227));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 3,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 10, 19, 6, 35, 811, DateTimeKind.Utc).AddTicks(1228));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 4,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 10, 19, 6, 35, 811, DateTimeKind.Utc).AddTicks(1229));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 5,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 10, 19, 6, 35, 811, DateTimeKind.Utc).AddTicks(1230));

            migrationBuilder.UpdateData(
                table: "BookingStatuses",
                keyColumn: "Id",
                keyValue: 6,
                column: "CreatedAt",
                value: new DateTime(2026, 7, 10, 19, 6, 35, 811, DateTimeKind.Utc).AddTicks(1230));

            migrationBuilder.UpdateData(
                table: "PlatformFeeConfigs",
                keyColumn: "Id",
                keyValue: 1,
                column: "CancellationFeePercent",
                value: 10m);

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
                name: "Payments");

            migrationBuilder.DropColumn(
                name: "CancellationFeePercent",
                table: "PlatformFeeConfigs");

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
        }
    }
}
