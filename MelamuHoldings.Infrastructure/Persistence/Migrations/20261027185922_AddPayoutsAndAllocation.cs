using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutsAndAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuotedRate",
                schema: "finance",
                table: "LedgerTransactions",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                schema: "finance",
                table: "LedgerEntries",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                // Every entry recorded before fees existed moved principal
                defaultValue: "Principal");

            migrationBuilder.CreateTable(
                name: "AllocationPolicies",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationPolicies_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    RequestedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ApprovedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedIntoAccountId = table.Column<int>(type: "int", nullable: true),
                    CreditedCurrency = table.Column<string>(type: "char(3)", nullable: true),
                    CreditedAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    FeeAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    IncomeTransactionId = table.Column<int>(type: "int", nullable: true),
                    EffectiveRate = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    RejectedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payouts_FinancialAccounts_ReceivedIntoAccountId",
                        column: x => x.ReceivedIntoAccountId,
                        principalSchema: "finance",
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payouts_LedgerTransactions_IncomeTransactionId",
                        column: x => x.IncomeTransactionId,
                        principalSchema: "finance",
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payouts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllocationPolicyLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllocationPolicyId = table.Column<int>(type: "int", nullable: false),
                    FundId = table.Column<int>(type: "int", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationPolicyLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationPolicyLines_AllocationPolicies_AllocationPolicyId",
                        column: x => x.AllocationPolicyId,
                        principalSchema: "finance",
                        principalTable: "AllocationPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationPolicyLines_Funds_FundId",
                        column: x => x.FundId,
                        principalSchema: "finance",
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Allocations",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayoutId = table.Column<int>(type: "int", nullable: false),
                    AllocationPolicyId = table.Column<int>(type: "int", nullable: false),
                    PolicyVersion = table.Column<int>(type: "int", nullable: false),
                    SourceAccountId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedByUserId = table.Column<int>(type: "int", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastSettledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allocations_AllocationPolicies_AllocationPolicyId",
                        column: x => x.AllocationPolicyId,
                        principalSchema: "finance",
                        principalTable: "AllocationPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Allocations_FinancialAccounts_SourceAccountId",
                        column: x => x.SourceAccountId,
                        principalSchema: "finance",
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Allocations_Payouts_PayoutId",
                        column: x => x.PayoutId,
                        principalSchema: "finance",
                        principalTable: "Payouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Allocations_Users_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllocationLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllocationId = table.Column<int>(type: "int", nullable: false),
                    FundId = table.Column<int>(type: "int", nullable: false),
                    FundName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationLines_Allocations_AllocationId",
                        column: x => x.AllocationId,
                        principalSchema: "finance",
                        principalTable: "Allocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationLines_Funds_FundId",
                        column: x => x.FundId,
                        principalSchema: "finance",
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllocationSettlements",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllocationLineId = table.Column<int>(type: "int", nullable: false),
                    LedgerTransactionId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationSettlements_AllocationLines_AllocationLineId",
                        column: x => x.AllocationLineId,
                        principalSchema: "finance",
                        principalTable: "AllocationLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationSettlements_LedgerTransactions_LedgerTransactionId",
                        column: x => x.LedgerTransactionId,
                        principalSchema: "finance",
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "finance",
                table: "AllocationPolicies",
                columns: new[] { "Id", "ActivatedAt", "CreatedAt", "CreatedByUserId", "IsActive", "Version" },
                values: new object[] { 1, new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Utc), null, true, 1 });

            migrationBuilder.InsertData(
                schema: "finance",
                table: "AllocationPolicyLines",
                columns: new[] { "Id", "AllocationPolicyId", "FundId", "Percentage", "Position" },
                values: new object[,]
                {
                    { 1, 1, 2, 40m, 1 },
                    { 2, 1, 3, 30m, 2 },
                    { 3, 1, 4, 20m, 3 },
                    { 4, 1, 5, 10m, 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationLines_AllocationId",
                schema: "finance",
                table: "AllocationLines",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationLines_FundId",
                schema: "finance",
                table: "AllocationLines",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationPolicies_CreatedByUserId",
                schema: "finance",
                table: "AllocationPolicies",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationPolicies_IsActive",
                schema: "finance",
                table: "AllocationPolicies",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationPolicies_Version",
                schema: "finance",
                table: "AllocationPolicies",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AllocationPolicyLines_AllocationPolicyId_FundId",
                schema: "finance",
                table: "AllocationPolicyLines",
                columns: new[] { "AllocationPolicyId", "FundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AllocationPolicyLines_FundId",
                schema: "finance",
                table: "AllocationPolicyLines",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_AllocationPolicyId",
                schema: "finance",
                table: "Allocations",
                column: "AllocationPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_ConfirmedByUserId",
                schema: "finance",
                table: "Allocations",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_IdempotencyKey",
                schema: "finance",
                table: "Allocations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_PayoutId",
                schema: "finance",
                table: "Allocations",
                column: "PayoutId",
                unique: true,
                filter: "[CancelledAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_SourceAccountId",
                schema: "finance",
                table: "Allocations",
                column: "SourceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationSettlements_AllocationLineId",
                schema: "finance",
                table: "AllocationSettlements",
                column: "AllocationLineId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationSettlements_LedgerTransactionId",
                schema: "finance",
                table: "AllocationSettlements",
                column: "LedgerTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_CreatedByUserId",
                schema: "finance",
                table: "Payouts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_IdempotencyKey",
                schema: "finance",
                table: "Payouts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_IncomeTransactionId",
                schema: "finance",
                table: "Payouts",
                column: "IncomeTransactionId",
                unique: true,
                filter: "[IncomeTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_ReceivedIntoAccountId",
                schema: "finance",
                table: "Payouts",
                column: "ReceivedIntoAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_Status",
                schema: "finance",
                table: "Payouts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllocationPolicyLines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "AllocationSettlements",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "AllocationLines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Allocations",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "AllocationPolicies",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Payouts",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "QuotedRate",
                schema: "finance",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "Role",
                schema: "finance",
                table: "LedgerEntries");
        }
    }
}
