using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyRulesAndChecklists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Strategies become owned by one trader each (PLAN §4, docs/TRADING_JOURNAL_DESIGN.md §6).
            // UserId starts nullable so existing rows can be given an owner before it becomes required.
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Strategies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Strategies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Strategies",
                type: "datetime2",
                nullable: true);

            // Descriptions may be empty (NULL) from now on
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Strategies",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // Backfill, in order:
            // 1. Each strategy goes to the lowest-numbered trader whose trades use it.
            // 2. Every other trader using it gets their own copy (same name, description and creation time),
            //    and their trades move to it.
            // 3. A strategy no trade uses has no owner to give it to; it holds only a name, so it is removed.
            // 4. Names are trimmed and cut to 100 characters, descriptions to 500 (blank becomes NULL).
            // 5. Names that now repeat for one trader (case-insensitive, by collation) are merged into the
            //    lowest id, with their trades moved to it.
            migrationBuilder.Sql(@"
UPDATE s SET UserId = (SELECT MIN(t.UserId) FROM Trades t WHERE t.StrategyId = s.Id)
FROM Strategies s;

INSERT INTO Strategies (Name, Description, CreatedAt, Status, UserId)
SELECT DISTINCT s.Name, s.Description, s.CreatedAt, 'Active', t.UserId
FROM Strategies s JOIN Trades t ON t.StrategyId = s.Id
WHERE t.UserId <> s.UserId;

UPDATE t SET StrategyId = c.Id
FROM Trades t
JOIN Strategies s ON s.Id = t.StrategyId
JOIN Strategies c ON c.UserId = t.UserId AND c.Name = s.Name AND c.CreatedAt = s.CreatedAt AND c.Id <> s.Id
WHERE t.UserId <> s.UserId;

DELETE FROM Strategies WHERE UserId IS NULL;

UPDATE Strategies SET
    Name = LEFT(LTRIM(RTRIM(Name)), 100),
    Description = CASE WHEN LTRIM(RTRIM(Description)) = '' THEN NULL ELSE LEFT(LTRIM(RTRIM(Description)), 500) END;

UPDATE t SET StrategyId = k.KeepId
FROM Trades t
JOIN Strategies s ON s.Id = t.StrategyId
JOIN (SELECT UserId, Name, MIN(Id) AS KeepId FROM Strategies GROUP BY UserId, Name) k
  ON k.UserId = s.UserId AND k.Name = s.Name
WHERE s.Id <> k.KeepId;

DELETE s FROM Strategies s
JOIN (SELECT UserId, Name, MIN(Id) AS KeepId FROM Strategies GROUP BY UserId, Name) k
  ON k.UserId = s.UserId AND k.Name = s.Name
WHERE s.Id <> k.KeepId;
");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Strategies",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Strategies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Strategies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "StrategyRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StrategyId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyRules_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeChecklists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    StrategyId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeChecklists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeChecklists_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeChecklists_Trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeChecklistId = table.Column<int>(type: "int", nullable: false),
                    StrategyRuleId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistItems_StrategyRules_StrategyRuleId",
                        column: x => x.StrategyRuleId,
                        principalTable: "StrategyRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChecklistItems_TradeChecklists_TradeChecklistId",
                        column: x => x.TradeChecklistId,
                        principalTable: "TradeChecklists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChecklistItemId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChecklistResponses_ChecklistItems_ChecklistItemId",
                        column: x => x.ChecklistItemId,
                        principalTable: "ChecklistItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Strategies_UserId_Name",
                table: "Strategies",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItems_StrategyRuleId",
                table: "ChecklistItems",
                column: "StrategyRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItems_TradeChecklistId",
                table: "ChecklistItems",
                column: "TradeChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistResponses_ChecklistItemId_Sequence",
                table: "ChecklistResponses",
                columns: new[] { "ChecklistItemId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyRules_StrategyId",
                table: "StrategyRules",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeChecklists_StrategyId",
                table: "TradeChecklists",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeChecklists_TradeId_StrategyId",
                table: "TradeChecklists",
                columns: new[] { "TradeId", "StrategyId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Strategies_Users_UserId",
                table: "Strategies",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        // Removes the rules, checklists and new columns. Strategies stay split per trader and orphans stay
        // removed: each row is still a valid strategy for the older schema.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Strategies_Users_UserId",
                table: "Strategies");

            migrationBuilder.DropTable(
                name: "ChecklistResponses");

            migrationBuilder.DropTable(
                name: "ChecklistItems");

            migrationBuilder.DropTable(
                name: "StrategyRules");

            migrationBuilder.DropTable(
                name: "TradeChecklists");

            migrationBuilder.DropIndex(
                name: "IX_Strategies_UserId_Name",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Strategies");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Strategies");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Strategies",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Strategies",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
