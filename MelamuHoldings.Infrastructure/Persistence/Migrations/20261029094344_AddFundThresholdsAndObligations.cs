using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundThresholdsAndObligations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Minimum",
                schema: "finance",
                table: "Funds",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Target",
                schema: "finance",
                table: "Funds",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThresholdCurrency",
                schema: "finance",
                table: "Funds",
                type: "char(3)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Obligations",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Recurrence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Obligations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObligationPayments",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObligationId = table.Column<int>(type: "int", nullable: false),
                    LedgerTransactionId = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObligationPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObligationPayments_LedgerTransactions_LedgerTransactionId",
                        column: x => x.LedgerTransactionId,
                        principalSchema: "finance",
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ObligationPayments_Obligations_ObligationId",
                        column: x => x.ObligationId,
                        principalSchema: "finance",
                        principalTable: "Obligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "finance",
                table: "Funds",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Minimum", "Target", "ThresholdCurrency" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                schema: "finance",
                table: "Funds",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Minimum", "Target", "ThresholdCurrency" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                schema: "finance",
                table: "Funds",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Minimum", "Target", "ThresholdCurrency" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                schema: "finance",
                table: "Funds",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Minimum", "Target", "ThresholdCurrency" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                schema: "finance",
                table: "Funds",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Minimum", "Target", "ThresholdCurrency" },
                values: new object[] { null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_ObligationPayments_LedgerTransactionId",
                schema: "finance",
                table: "ObligationPayments",
                column: "LedgerTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObligationPayments_ObligationId",
                schema: "finance",
                table: "ObligationPayments",
                column: "ObligationId");

            migrationBuilder.CreateIndex(
                name: "IX_Obligations_Name_Active",
                schema: "finance",
                table: "Obligations",
                column: "Name",
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ObligationPayments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Obligations",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "Minimum",
                schema: "finance",
                table: "Funds");

            migrationBuilder.DropColumn(
                name: "Target",
                schema: "finance",
                table: "Funds");

            migrationBuilder.DropColumn(
                name: "ThresholdCurrency",
                schema: "finance",
                table: "Funds");
        }
    }
}
