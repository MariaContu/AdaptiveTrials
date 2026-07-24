using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AdaptiveTrials.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeMissionDifficulties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar uma pequena quantidade de inimigos.", "{\"enemies\":3,\"boss\":false}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar uma quantidade intermediária de inimigos.", "{\"enemies\":6,\"boss\":false}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Derrotar um inimigo de elite.", 3 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Difficulty",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Resistir por um período intermediário.", 2 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Resistir por um período prolongado sob alta pressão.", 3 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Defender um objeto durante poucas ondas.", 1 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Defender um objeto contra várias ondas.", 3 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Buscar uma quantidade intermediária de itens.", 2 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Difficulty",
                value: 3);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Chegar ao destino por uma rota curta e pouco perigosa.", "{\"distance\":\"short\",\"checkpoints\":2,\"hazards\":2,\"maxFailures\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "Description", "Difficulty", "ParametersJson" },
                values: new object[] { "Chegar ao destino por uma rota longa e perigosa.", 3, "{\"distance\":\"long\",\"checkpoints\":5,\"hazards\":6,\"maxFailures\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "Difficulty", "ParametersJson" },
                values: new object[] { 1, "{\"enemies\":2,\"enemySpeed\":65,\"detectionRadius\":70,\"maxFailures\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "Description", "Difficulty", "ParametersJson" },
                values: new object[] { "Evitar várias patrulhas em uma missão de alta dificuldade.", 3, "{\"enemies\":6,\"enemySpeed\":110,\"detectionRadius\":115,\"maxFailures\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Repetir uma sequência de tamanho intermediário.", 2 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 18,
                column: "Difficulty",
                value: 3);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 19,
                column: "Difficulty",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Resolver uma conexão complexa entre vários pontos.", 3 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 21,
                column: "Difficulty",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Decifrar um código com poucas pistas disponíveis.", 3 });

            migrationBuilder.InsertData(
                table: "Missions",
                columns: new[] { "Id", "Description", "Difficulty", "Name", "ParametersJson", "Template", "Type" },
                values: new object[,]
                {
                    { 23, "Defender um objeto durante uma quantidade intermediária de ondas.", 2, "Defesa Intermediária", "{\"waves\":3}", "Defender Objeto", 1 },
                    { 24, "Chegar ao destino por uma rota de complexidade intermediária.", 2, "Navegação Intermediária", "{\"distance\":\"medium\",\"checkpoints\":3,\"hazards\":4,\"maxFailures\":3}", "Chegar ao Destino", 2 },
                    { 25, "Evitar patrulhas de velocidade e alcance intermediários.", 2, "Stealth Intermediário", "{\"enemies\":4,\"enemySpeed\":85,\"detectionRadius\":90,\"maxFailures\":3}", "Evitar Inimigos", 2 },
                    { 26, "Resolver uma conexão de complexidade intermediária.", 2, "Conexão Intermediária", "{\"pieces\":6}", "Conectar Pontos", 3 },
                    { 27, "Decifrar um código com uma quantidade intermediária de pistas.", 2, "Código Intermediário", "{\"clues\":2}", "Decifrar Código", 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar pequenos inimigos.", "{\"enemies\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar uma quantidade maior de inimigos.", "{\"enemies\":6}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Derrotar um inimigo forte.", 4 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Difficulty",
                value: 2);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Resistir por um período médio de tempo.", 3 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Resistir sob alta pressão.", 5 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Defender um objeto simples.", 2 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Defender um objeto contra muitas ondas.", 4 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Buscar uma quantidade maior de itens.", 3 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Difficulty",
                value: 5);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Chegar a um destino por um caminho simples.", "{\"distance\":\"short\"}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "Description", "Difficulty", "ParametersJson" },
                values: new object[] { "Chegar a um destino em um mapa mais complexo.", 4, "{\"distance\":\"long\"}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "Difficulty", "ParametersJson" },
                values: new object[] { 2, "{\"enemies\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "Description", "Difficulty", "ParametersJson" },
                values: new object[] { "Evitar vários inimigos em uma missão de alta dificuldade.", 5, "{\"enemies\":6}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Repetir uma sequência maior.", 3 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 18,
                column: "Difficulty",
                value: 5);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 19,
                column: "Difficulty",
                value: 2);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Resolver uma conexão mais complexa entre pontos.", 4 });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 21,
                column: "Difficulty",
                value: 2);

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "Description", "Difficulty" },
                values: new object[] { "Decifrar um código com poucas pistas.", 5 });
        }
    }
}
