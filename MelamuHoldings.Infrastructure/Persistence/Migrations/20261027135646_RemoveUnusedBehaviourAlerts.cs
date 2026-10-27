using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedBehaviourAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BehaviourAlerts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BehaviourAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AlertType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetectedOnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    TradeIds = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BehaviourAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BehaviourAlerts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BehaviourAlerts_UserId",
                table: "BehaviourAlerts",
                column: "UserId");
        }
    }
}
