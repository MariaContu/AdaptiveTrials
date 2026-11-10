using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdaptiveTrials.Infrastructure.Migrations
{
    public partial class AlignMissionParametersWithGameplay : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar o alvo protegido por poucos inimigos.", "{\"targetHealth\":5,\"guards\":1}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar um alvo mais resistente protegido por uma quantidade intermediária de inimigos.", "{\"targetHealth\":7,\"guards\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar um alvo de alta resistência protegido por vários inimigos.", "{\"targetHealth\":10,\"guards\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Defender o cristal durante poucas ondas, adaptando-se aos reposicionamentos após dano.", "{\"waves\":2,\"objectHealth\":14}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Defender o cristal durante uma quantidade intermediária de ondas, adaptando-se aos reposicionamentos após dano.", "{\"waves\":3,\"objectHealth\":12}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Defender o cristal contra várias ondas, adaptando-se aos reposicionamentos após dano.", "{\"waves\":5,\"objectHealth\":10}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Explorar um labirinto simples com visibilidade limitada para encontrar poucos objetos.", "{\"items\":3,\"lightScale\":3.0}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Explorar um labirinto intermediário com visibilidade reduzida para encontrar objetos distribuídos pelo cenário.", "{\"items\":6,\"lightScale\":2.35}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Explorar um labirinto complexo com baixa visibilidade para encontrar todos os objetos.", "{\"items\":10,\"lightScale\":1.75}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Percorrer uma rota curta seguindo checkpoints e atravessando poucos obstáculos temporizados.", "{\"checkpoints\":2,\"hazards\":2,\"lasers\":1,\"maxFailures\":4,\"safeSeconds\":2.8,\"warningSeconds\":1.15,\"activeSeconds\":1.0}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Percorrer uma rota intermediária seguindo checkpoints e sincronizando a travessia de obstáculos temporizados.", "{\"checkpoints\":3,\"hazards\":4,\"lasers\":3,\"maxFailures\":3,\"safeSeconds\":2.0,\"warningSeconds\":0.8,\"activeSeconds\":1.25}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Percorrer uma rota complexa com vários checkpoints, lasers e áreas energizadas de janelas reduzidas.", "{\"checkpoints\":5,\"hazards\":6,\"lasers\":5,\"maxFailures\":2,\"safeSeconds\":1.35,\"warningSeconds\":0.55,\"activeSeconds\":1.55}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Atravessar uma rota de furtividade com poucas patrulhas, usando cobertura e pontos seguros.", "{\"enemies\":2,\"enemySpeed\":65,\"detectionRadius\":70,\"coneAngle\":52,\"suspicionSeconds\":1.35,\"safePoints\":1,\"maxFailures\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Atravessar uma rota de furtividade intermediária com múltiplas patrulhas, cobertura e pontos seguros.", "{\"enemies\":4,\"enemySpeed\":85,\"detectionRadius\":90,\"coneAngle\":68,\"suspicionSeconds\":0.9,\"safePoints\":2,\"maxFailures\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Atravessar uma rota de furtividade avançada com várias patrulhas, cones de visão amplos e menor tempo de reação.", "{\"enemies\":6,\"enemySpeed\":110,\"detectionRadius\":115,\"coneAngle\":88,\"suspicionSeconds\":0.55,\"safePoints\":3,\"maxFailures\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Repetir uma sequência curta de símbolos.", "{\"sequenceSize\":3,\"maxFailures\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Repetir uma sequência de tamanho intermediário.", "{\"sequenceSize\":5,\"maxFailures\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Repetir uma sequência longa de alta exigência de memória.", "{\"sequenceSize\":8,\"maxFailures\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Conectar poucos pares de pontos respeitando as regras do tabuleiro.", "{\"pieces\":4,\"maxFailures\":5}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Conectar uma quantidade intermediária de pares de pontos respeitando as regras do tabuleiro.", "{\"pieces\":6,\"maxFailures\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Conectar vários pares de pontos em um tabuleiro de maior complexidade.", "{\"pieces\":8,\"maxFailures\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Descobrir uma sequência oculta de cinco símbolos em até seis tentativas.", "{\"boards\":1,\"attempts\":6}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Descobrir duas sequências ocultas simultaneamente em até sete tentativas.", "{\"boards\":2,\"attempts\":7}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Descobrir quatro sequências ocultas simultaneamente em até nove tentativas.", "{\"boards\":4,\"attempts\":9}" });

        }

        protected override void Down(MigrationBuilder migrationBuilder)
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
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Derrotar um inimigo de elite.", "{\"enemies\":1,\"boss\":true}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Defender um objeto durante poucas ondas.", "{\"waves\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Defender um objeto durante uma quantidade intermediária de ondas.", "{\"waves\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Defender um objeto contra várias ondas.", "{\"waves\":5}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Buscar poucos itens pelo cenário.", "{\"items\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Buscar uma quantidade intermediária de itens.", "{\"items\":6}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Buscar muitos itens em uma missão de alta complexidade.", "{\"items\":10}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Chegar ao destino por uma rota curta e pouco perigosa.", "{\"distance\":\"short\",\"checkpoints\":2,\"hazards\":2,\"maxFailures\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Chegar ao destino por uma rota de complexidade intermediária.", "{\"distance\":\"medium\",\"checkpoints\":3,\"hazards\":4,\"maxFailures\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Chegar ao destino por uma rota longa e perigosa.", "{\"distance\":\"long\",\"checkpoints\":5,\"hazards\":6,\"maxFailures\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Evitar poucos inimigos durante o percurso.", "{\"enemies\":2,\"enemySpeed\":65,\"detectionRadius\":70,\"maxFailures\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Evitar patrulhas de velocidade e alcance intermediários.", "{\"enemies\":4,\"enemySpeed\":85,\"detectionRadius\":90,\"maxFailures\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Evitar várias patrulhas em uma missão de alta dificuldade.", "{\"enemies\":6,\"enemySpeed\":110,\"detectionRadius\":115,\"maxFailures\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Repetir uma sequência curta.", "{\"sequenceSize\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Repetir uma sequência de tamanho intermediário.", "{\"sequenceSize\":5}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Repetir uma sequência de alta exigência de memória.", "{\"sequenceSize\":8}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Resolver uma conexão simples entre pontos.", "{\"pieces\":4}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Resolver uma conexão de complexidade intermediária.", "{\"pieces\":6}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Resolver uma conexão complexa entre vários pontos.", "{\"pieces\":8}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Decifrar um código com várias pistas disponíveis.", "{\"clues\":3}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Decifrar um código com uma quantidade intermediária de pistas.", "{\"clues\":2}" });

            migrationBuilder.UpdateData(
                table: "Missions",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "Description", "ParametersJson" },
                values: new object[] { "Decifrar um código com poucas pistas disponíveis.", "{\"clues\":1}" });

        }
    }
}