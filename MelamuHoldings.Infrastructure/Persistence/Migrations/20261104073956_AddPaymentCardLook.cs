using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelamuHoldings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCardLook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Finish",
                schema: "finance",
                table: "PaymentCards",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Navy");

            migrationBuilder.AddColumn<string>(
                name: "Network",
                schema: "finance",
                table: "PaymentCards",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotStated");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Finish",
                schema: "finance",
                table: "PaymentCards");

            migrationBuilder.DropColumn(
                name: "Network",
                schema: "finance",
                table: "PaymentCards");
        }
    }
}
