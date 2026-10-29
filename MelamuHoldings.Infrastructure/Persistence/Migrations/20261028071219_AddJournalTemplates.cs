using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JournalTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalTemplates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JournalTemplateId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Schema = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalTemplateVersions_JournalTemplates_JournalTemplateId",
                        column: x => x.JournalTemplateId,
                        principalTable: "JournalTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeJournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    JournalTemplateVersionId = table.Column<int>(type: "int", nullable: false),
                    Answers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeJournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeJournalEntries_JournalTemplateVersions_JournalTemplateVersionId",
                        column: x => x.JournalTemplateVersionId,
                        principalTable: "JournalTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeJournalEntries_Trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalTemplates_OneDefault",
                table: "JournalTemplates",
                column: "UserId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_JournalTemplateVersions_JournalTemplateId_Version",
                table: "JournalTemplateVersions",
                columns: new[] { "JournalTemplateId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeJournalEntries_JournalTemplateVersionId",
                table: "TradeJournalEntries",
                column: "JournalTemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeJournalEntries_TradeId",
                table: "TradeJournalEntries",
                column: "TradeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeJournalEntries");

            migrationBuilder.DropTable(
                name: "JournalTemplateVersions");

            migrationBuilder.DropTable(
                name: "JournalTemplates");
        }
    }
}
