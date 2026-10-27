using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialAccounts",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Ownership = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Institution = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Funds",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerTransactions",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    ReversesTransactionId = table.Column<int>(type: "int", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerTransactions_LedgerTransactions_ReversesTransactionId",
                        column: x => x.ReversesTransactionId,
                        principalSchema: "finance",
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerTransactions_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LedgerTransactionId = table.Column<int>(type: "int", nullable: false),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false),
                    FundId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalSchema: "finance",
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Funds_FundId",
                        column: x => x.FundId,
                        principalSchema: "finance",
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_LedgerTransactions_LedgerTransactionId",
                        column: x => x.LedgerTransactionId,
                        principalSchema: "finance",
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "finance",
                table: "Funds",
                columns: new[] { "Id", "CreatedAt", "Description", "IsSystem", "Name" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Money received but not yet earmarked for a purpose.", true, "Unallocated" },
                    { 2, new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Day-to-day running costs of the business.", false, "Operating Capital" },
                    { 3, new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Reserve for setbacks: essential expenses and restoring trading capacity.", false, "Business Continuity" },
                    { 4, new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Long-term investment contributions.", false, "Wealth Fund" },
                    { 5, new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Money set aside to pay out to the owner.", false, "Owner's Distribution" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                schema: "finance",
                table: "AuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UserId",
                schema: "finance",
                table: "AuditEvents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccounts_Name",
                schema: "finance",
                table: "FinancialAccounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Funds_Name",
                schema: "finance",
                table: "Funds",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_FinancialAccountId_FundId",
                schema: "finance",
                table: "LedgerEntries",
                columns: new[] { "FinancialAccountId", "FundId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_FundId",
                schema: "finance",
                table: "LedgerEntries",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_LedgerTransactionId",
                schema: "finance",
                table: "LedgerEntries",
                column: "LedgerTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_IdempotencyKey",
                schema: "finance",
                table: "LedgerTransactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_RecordedByUserId",
                schema: "finance",
                table: "LedgerTransactions",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_ReversesTransactionId",
                schema: "finance",
                table: "LedgerTransactions",
                column: "ReversesTransactionId",
                unique: true,
                filter: "[ReversesTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_TransactionDate",
                schema: "finance",
                table: "LedgerTransactions",
                column: "TransactionDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "LedgerEntries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "FinancialAccounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Funds",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "LedgerTransactions",
                schema: "finance");
        }
    }
}
