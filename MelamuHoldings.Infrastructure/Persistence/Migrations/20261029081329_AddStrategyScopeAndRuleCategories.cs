using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyScopeAndRuleCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "StrategyRules",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "General");   // existing rules and checklist items are not about one stage

            migrationBuilder.AddColumn<int>(
                name: "DefaultTemplateId",
                table: "Strategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instruments",
                table: "Strategies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Sessions",
                table: "Strategies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ChecklistItems",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "General");   // existing rules and checklist items are not about one stage

            migrationBuilder.CreateIndex(
                name: "IX_Strategies_DefaultTemplateId",
                table: "Strategies",
                column: "DefaultTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Strategies_JournalTemplates_DefaultTemplateId",
                table: "Strategies",
                column: "DefaultTemplateId",
                principalTable: "JournalTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Strategies_JournalTemplates_DefaultTemplateId",
                table: "Strategies");

            migrationBuilder.DropIndex(
                name: "IX_Strategies_DefaultTemplateId",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "StrategyRules");

            migrationBuilder.DropColumn(
                name: "DefaultTemplateId",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "Instruments",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "Sessions",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "ChecklistItems");
        }
    }
}
