using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropAccountFeesAndPayoutLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PropFirmAccountId",
                schema: "finance",
                table: "Payouts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PropAccountFees",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PropFirmAccountId = table.Column<int>(type: "int", nullable: false),
                    LedgerTransactionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropAccountFees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropAccountFees_LedgerTransactions_LedgerTransactionId",
                        column: x => x.LedgerTransactionId,
                        principalSchema: "finance",
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PropAccountFees_PropFirmAccounts_PropFirmAccountId",
                        column: x => x.PropFirmAccountId,
                        principalTable: "PropFirmAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_PropFirmAccountId",
                schema: "finance",
                table: "Payouts",
                column: "PropFirmAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PropAccountFees_LedgerTransactionId",
                schema: "finance",
                table: "PropAccountFees",
                column: "LedgerTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropAccountFees_PropFirmAccountId",
                schema: "finance",
                table: "PropAccountFees",
                column: "PropFirmAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payouts_PropFirmAccounts_PropFirmAccountId",
                schema: "finance",
                table: "Payouts",
                column: "PropFirmAccountId",
                principalTable: "PropFirmAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payouts_PropFirmAccounts_PropFirmAccountId",
                schema: "finance",
                table: "Payouts");

            migrationBuilder.DropTable(
                name: "PropAccountFees",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "IX_Payouts_PropFirmAccountId",
                schema: "finance",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "PropFirmAccountId",
                schema: "finance",
                table: "Payouts");
        }
    }
}
