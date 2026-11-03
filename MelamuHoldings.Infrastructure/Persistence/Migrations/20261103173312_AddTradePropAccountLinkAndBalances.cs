using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradePropAccountLinkAndBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PropFirmAccountId",
                table: "Trades",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PropAccountBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PropFirmAccountId = table.Column<int>(type: "int", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    DrawdownLevel = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropAccountBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropAccountBalances_PropFirmAccounts_PropFirmAccountId",
                        column: x => x.PropFirmAccountId,
                        principalTable: "PropFirmAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_PropFirmAccountId",
                table: "Trades",
                column: "PropFirmAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PropAccountBalances_PropFirmAccountId_AsOf",
                table: "PropAccountBalances",
                columns: new[] { "PropFirmAccountId", "AsOf" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Trades_PropFirmAccounts_PropFirmAccountId",
                table: "Trades",
                column: "PropFirmAccountId",
                principalTable: "PropFirmAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trades_PropFirmAccounts_PropFirmAccountId",
                table: "Trades");

            migrationBuilder.DropTable(
                name: "PropAccountBalances");

            migrationBuilder.DropIndex(
                name: "IX_Trades_PropFirmAccountId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "PropFirmAccountId",
                table: "Trades");
        }
    }
}
