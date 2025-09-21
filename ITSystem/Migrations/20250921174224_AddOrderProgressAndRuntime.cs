using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderProgressAndRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Progress",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RuntimeSeconds",
                table: "Orders",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Progress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RuntimeSeconds",
                table: "Orders");
        }
    }
}
