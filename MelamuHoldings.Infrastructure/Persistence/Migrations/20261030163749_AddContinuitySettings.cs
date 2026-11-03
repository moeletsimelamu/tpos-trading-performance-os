using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContinuitySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContinuitySettings",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ContinuityFundId = table.Column<int>(type: "int", nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    MonthlyEssentialExpense = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    RestorationFundId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContinuitySettings", x => x.Id);
                    table.CheckConstraint("CK_ContinuitySettings_Singleton", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_ContinuitySettings_Funds_ContinuityFundId",
                        column: x => x.ContinuityFundId,
                        principalSchema: "finance",
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContinuitySettings_Funds_RestorationFundId",
                        column: x => x.RestorationFundId,
                        principalSchema: "finance",
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "finance",
                table: "ContinuitySettings",
                columns: new[] { "Id", "ContinuityFundId", "Currency", "MonthlyEssentialExpense", "Months", "RestorationFundId", "UpdatedAt" },
                values: new object[] { 1, 3, "ZAR", null, 24, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_ContinuitySettings_ContinuityFundId",
                schema: "finance",
                table: "ContinuitySettings",
                column: "ContinuityFundId");

            migrationBuilder.CreateIndex(
                name: "IX_ContinuitySettings_RestorationFundId",
                schema: "finance",
                table: "ContinuitySettings",
                column: "RestorationFundId");

            // D6: restoration capital gets its own fund, so no money counts towards both continuity and restoration.
            // Created only if the owner has not already made one with that name; either way the settings point at it.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [finance].[Funds] WHERE [Name] = N'Trading Restoration')
                    INSERT INTO [finance].[Funds] ([Name], [Description], [IsSystem], [CreatedAt])
                    VALUES (N'Trading Restoration', N'Capital to restore trading after a setback; separate from the continuity reserve.', 0, SYSUTCDATETIME());
                UPDATE [finance].[ContinuitySettings]
                SET [RestorationFundId] = (SELECT [Id] FROM [finance].[Funds] WHERE [Name] = N'Trading Restoration')
                WHERE [Id] = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The Trading Restoration fund is left in place: it may hold money by now
            migrationBuilder.DropTable(
                name: "ContinuitySettings",
                schema: "finance");
        }
    }
}
