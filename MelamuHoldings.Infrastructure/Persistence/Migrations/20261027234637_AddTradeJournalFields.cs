using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeJournalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TradeDuration",
                table: "Trades",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "RiskAmount",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "RR",
                table: "Trades",
                type: "decimal(8,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "PnL",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "ExitPrice",
                table: "Trades",
                type: "decimal(18,5)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,5)");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "EntryTime",
                table: "Trades",
                type: "time",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time");

            migrationBuilder.AlterColumn<decimal>(
                name: "EntryPrice",
                table: "Trades",
                type: "decimal(18,5)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,5)");

            migrationBuilder.AlterColumn<string>(
                name: "Direction",
                table: "Trades",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Trades",
                type: "char(3)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExitDate",
                table: "Trades",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ExitTime",
                table: "Trades",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Fees",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Trades",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PositionSize",
                table: "Trades",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "Trades",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Trades",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Trades",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                // Every trade recorded before the journal (all imported) is a complete, closed trade
                defaultValue: "Closed");

            migrationBuilder.AddColumn<decimal>(
                name: "StopLoss",
                table: "Trades",
                type: "decimal(18,5)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfit",
                table: "Trades",
                type: "decimal(18,5)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradingAccount",
                table: "Trades",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Trades",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_UserId_IdempotencyKey",
                table: "Trades",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_UserId_Status",
                table: "Trades",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_UserId_IdempotencyKey",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Trades_UserId_Status",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "ExitDate",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "ExitTime",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "Fees",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "PositionSize",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "StopLoss",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "TakeProfit",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "TradingAccount",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Trades");

            migrationBuilder.AlterColumn<int>(
                name: "TradeDuration",
                table: "Trades",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "RiskAmount",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "RR",
                table: "Trades",
                type: "decimal(8,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(8,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PnL",
                table: "Trades",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ExitPrice",
                table: "Trades",
                type: "decimal(18,5)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,5)",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "EntryTime",
                table: "Trades",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                oldClrType: typeof(TimeOnly),
                oldType: "time",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "EntryPrice",
                table: "Trades",
                type: "decimal(18,5)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,5)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Direction",
                table: "Trades",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
