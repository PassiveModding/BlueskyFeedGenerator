using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Common.Migrations
{
    /// <inheritdoc />
    public partial class Languages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Langs",
                table: "Posts");

            migrationBuilder.AddColumn<string[]>(
                name: "Languages",
                table: "Posts",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Languages",
                table: "Posts");

            migrationBuilder.AddColumn<string>(
                name: "Langs",
                table: "Posts",
                type: "text",
                nullable: true);
        }
    }
}
