using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AdaptiveTrials.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedMissionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Missions",
                columns: new[] { "Id", "Description", "Difficulty", "Name", "ParametersJson", "Template", "Type" },
                values: new object[,]
                {
                    { 1, "Derrotar pequenos inimigos.", 1, "Caça Simples", "{\"enemies\":3}", "Eliminar Alvo", 1 },
                    { 2, "Derrotar uma quantidade maior de inimigos.", 2, "Caça Média", "{\"enemies\":6}", "Eliminar Alvo", 1 },
                    { 3, "Derrotar um inimigo forte.", 4, "Caça Elite", "{\"enemies\":1,\"boss\":true}", "Eliminar Alvo", 1 },
                    { 4, "Resistir por um curto período de tempo.", 2, "Sobrevivência Curta", "{\"timeSeconds\":30}", "Sobreviver", 1 },
                    { 5, "Resistir por um período médio de tempo.", 3, "Sobrevivência Média", "{\"timeSeconds\":60}", "Sobreviver", 1 },
                    { 6, "Resistir sob alta pressão.", 5, "Sobrevivência Extrema", "{\"timeSeconds\":120}", "Sobreviver", 1 },
                    { 7, "Defender um objeto simples.", 2, "Defesa Básica", "{\"waves\":2}", "Defender Objeto", 1 },
                    { 8, "Defender um objeto contra muitas ondas.", 4, "Defesa Avançada", "{\"waves\":5}", "Defender Objeto", 1 },
                    { 9, "Buscar poucos itens pelo cenário.", 1, "Exploração Simples", "{\"items\":3}", "Encontrar Objetos", 2 },
                    { 10, "Buscar uma quantidade maior de itens.", 3, "Exploração Média", "{\"items\":6}", "Encontrar Objetos", 2 },
                    { 11, "Buscar muitos itens em uma missão de alta complexidade.", 5, "Exploração Difícil", "{\"items\":10}", "Encontrar Objetos", 2 },
                    { 12, "Chegar a um destino por um caminho simples.", 1, "Navegação Simples", "{\"distance\":\"short\"}", "Chegar ao Destino", 2 },
                    { 13, "Chegar a um destino em um mapa mais complexo.", 4, "Navegação Complexa", "{\"distance\":\"long\"}", "Chegar ao Destino", 2 },
                    { 14, "Evitar poucos inimigos durante o percurso.", 2, "Stealth Básico", "{\"enemies\":2}", "Evitar Inimigos", 2 },
                    { 15, "Evitar vários inimigos em uma missão de alta dificuldade.", 5, "Stealth Avançado", "{\"enemies\":6}", "Evitar Inimigos", 2 },
                    { 16, "Repetir uma sequência curta.", 1, "Sequência Simples", "{\"sequenceSize\":3}", "Repetir Sequência", 3 },
                    { 17, "Repetir uma sequência maior.", 3, "Sequência Média", "{\"sequenceSize\":5}", "Repetir Sequência", 3 },
                    { 18, "Repetir uma sequência de alta exigência de memória.", 5, "Sequência Difícil", "{\"sequenceSize\":8}", "Repetir Sequência", 3 },
                    { 19, "Resolver uma conexão simples entre pontos.", 2, "Conexão Básica", "{\"pieces\":4}", "Conectar Pontos", 3 },
                    { 20, "Resolver uma conexão mais complexa entre pontos.", 4, "Conexão Avançada", "{\"pieces\":8}", "Conectar Pontos", 3 },
                    { 21, "Decifrar um código com várias pistas disponíveis.", 2, "Código Simples", "{\"clues\":3}", "Decifrar Código", 3 },
                    { 22, "Decifrar um código com poucas pistas.", 5, "Código Difícil", "{\"clues\":1}", "Decifrar Código", 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 22);
        }
    }
}
