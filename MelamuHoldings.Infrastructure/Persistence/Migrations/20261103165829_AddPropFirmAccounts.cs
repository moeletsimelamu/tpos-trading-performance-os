using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropFirmAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PropFirmAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Firm = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nickname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    AccountSize = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Login = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RunStartedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    StartingBalance = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropFirmAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropFirmAccounts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropAccountEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PropFirmAccountId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Login = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StartingBalance = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropAccountEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropAccountEvents_PropFirmAccounts_PropFirmAccountId",
                        column: x => x.PropFirmAccountId,
                        principalTable: "PropFirmAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropAccountRuleSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PropFirmAccountId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DrawdownModel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    TrailingLocksAtStart = table.Column<bool>(type: "bit", nullable: false),
                    DailyLossLimit = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ProfitTarget = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    MinimumTradingDays = table.Column<int>(type: "int", nullable: true),
                    PayoutSplitPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    PurchaseFee = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ResetFee = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    MonthlyFee = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropAccountRuleSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropAccountRuleSets_PropFirmAccounts_PropFirmAccountId",
                        column: x => x.PropFirmAccountId,
                        principalTable: "PropFirmAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropAccountEvents_PropFirmAccountId",
                table: "PropAccountEvents",
                column: "PropFirmAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PropAccountRuleSets_PropFirmAccountId_Version",
                table: "PropAccountRuleSets",
                columns: new[] { "PropFirmAccountId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropFirmAccounts_UserId_Nickname_Open",
                table: "PropFirmAccounts",
                columns: new[] { "UserId", "Nickname" },
                unique: true,
                filter: "[Status] <> 'Closed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PropAccountEvents");

            migrationBuilder.DropTable(
                name: "PropAccountRuleSets");

            migrationBuilder.DropTable(
                name: "PropFirmAccounts");
        }
    }
}
