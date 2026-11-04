using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                schema: "finance",
                table: "FinancialAccounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotStated");

            migrationBuilder.AddColumn<int>(
                name: "HomeFundId",
                schema: "finance",
                table: "FinancialAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OpenedOn",
                schema: "finance",
                table: "FinancialAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                schema: "finance",
                table: "FinancialAccounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxTreatment",
                schema: "finance",
                table: "FinancialAccounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotStated");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccounts_HomeFundId",
                schema: "finance",
                table: "FinancialAccounts",
                column: "HomeFundId");

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialAccounts_Funds_HomeFundId",
                schema: "finance",
                table: "FinancialAccounts",
                column: "HomeFundId",
                principalSchema: "finance",
                principalTable: "Funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinancialAccounts_Funds_HomeFundId",
                schema: "finance",
                table: "FinancialAccounts");

            migrationBuilder.DropIndex(
                name: "IX_FinancialAccounts_HomeFundId",
                schema: "finance",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "finance",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "HomeFundId",
                schema: "finance",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "OpenedOn",
                schema: "finance",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "Reference",
                schema: "finance",
                table: "FinancialAccounts");

            migrationBuilder.DropColumn(
                name: "TaxTreatment",
                schema: "finance",
                table: "FinancialAccounts");
        }
    }
}
