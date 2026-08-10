using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdaptiveTrials.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Name",
                value: "Furtividade Básica");

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Name",
                value: "Furtividade Avançada");

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 25,
                column: "Name",
                value: "Furtividade Intermediária");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Name",
                value: "Stealth Básico");

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Name",
                value: "Stealth Avançado");

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 25,
                column: "Name",
                value: "Stealth Intermediário");
        }
    }
}
