using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdaptiveTrials.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSurvivalMissionsToWaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Description", "Name", "ParametersJson" },
                values: new object[] { "Sobreviver e eliminar três ondas de inimigos.", "Sobrevivência Inicial", "{\"waves\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Description", "Name", "ParametersJson" },
                values: new object[] { "Sobreviver e eliminar cinco ondas de dificuldade progressiva.", "Sobrevivência Intermediária", "{\"waves\":5}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Sobreviver e eliminar sete ondas de inimigos sob alta pressão.", "{\"waves\":7}" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Description", "Name", "ParametersJson" },
                values: new object[] { "Resistir por um curto período de tempo.", "Sobrevivência Curta", "{\"timeSeconds\":30}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Description", "Name", "ParametersJson" },
                values: new object[] { "Resistir por um período intermediário.", "Sobrevivência Média", "{\"timeSeconds\":60}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Resistir por um período prolongado sob alta pressão.", "{\"timeSeconds\":120}" });
        }
    }
}
