using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetsAndPlannedPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLines", x => x.Id);
                    table.CheckConstraint("CK_BudgetLines_FirstOfMonth", "DAY([Month]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "PlannedPurchases",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    FundId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LedgerTransactionId = table.Column<int>(type: "int", nullable: true),
                    PaidAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedPurchases_Funds_FundId",
                        column: x => x.FundId,
                        principalSchema: "finance",
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlannedPurchases_LedgerTransactions_LedgerTransactionId",
                        column: x => x.LedgerTransactionId,
                        principalSchema: "finance",
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_Month_Category_Currency",
                schema: "finance",
                table: "BudgetLines",
                columns: new[] { "Month", "Category", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlannedPurchases_FundId",
                schema: "finance",
                table: "PlannedPurchases",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedPurchases_LedgerTransactionId",
                schema: "finance",
                table: "PlannedPurchases",
                column: "LedgerTransactionId",
                unique: true,
                filter: "[LedgerTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedPurchases_Name_Open",
                schema: "finance",
                table: "PlannedPurchases",
                column: "Name",
                unique: true,
                filter: "[Status] IN ('Proposed', 'Committed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetLines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PlannedPurchases",
                schema: "finance");
        }
    }
}
