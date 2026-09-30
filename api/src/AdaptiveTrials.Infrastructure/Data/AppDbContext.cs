using AdaptiveTrials.Domain.Entities;
using AdaptiveTrials.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdaptiveTrials.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Player> Players =>
        Set<Player>();

    public DbSet<GameSession> Sessions =>
        Set<GameSession>();

    public DbSet<Mission> Missions =>
        Set<Mission>();

    public DbSet<BehaviorEvent> BehaviorEvents =>
        Set<BehaviorEvent>();

    public DbSet<Recommendation> Recommendations =>
        Set<Recommendation>();

    public DbSet<NormalizedProfile> NormalizedProfiles =>
        Set<NormalizedProfile>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Player>()
            .HasOne(player =>
                player.NormalizedProfile)
            .WithOne(profile =>
                profile.Player)
            .HasForeignKey<NormalizedProfile>(
                profile =>
                    profile.PlayerId);

        modelBuilder.Entity<Player>()
            .HasMany(player =>
                player.Sessions)
            .WithOne(session =>
                session.Player)
            .HasForeignKey(session =>
                session.PlayerId);

        modelBuilder.Entity<GameSession>()
            .HasMany(session =>
                session.BehaviorEvents)
            .WithOne(behaviorEvent =>
                behaviorEvent.Session)
            .HasForeignKey(behaviorEvent =>
                behaviorEvent.SessionId);

        modelBuilder.Entity<GameSession>()
            .HasMany(session =>
                session.Recommendations)
            .WithOne(recommendation =>
                recommendation.Session)
            .HasForeignKey(recommendation =>
                recommendation.SessionId);

        modelBuilder.Entity<Mission>()
            .HasMany(mission =>
                mission.BehaviorEvents)
            .WithOne(behaviorEvent =>
                behaviorEvent.Mission)
            .HasForeignKey(behaviorEvent =>
                behaviorEvent.MissionId);

        modelBuilder.Entity<Mission>()
            .HasMany(mission =>
                mission.Recommendations)
            .WithOne(recommendation =>
                recommendation.Mission)
            .HasForeignKey(recommendation =>
                recommendation.MissionId)
            .IsRequired(false);

        SeedMissions(modelBuilder);
    }

    private static void SeedMissions(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mission>().HasData(
            /*
             * COMBATE — ELIMINAR ALVO
             */
            new Mission
            {
                Id = 1,
                Name = "Caça Simples",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 1,
                ParametersJson =
                    "{\"targetHealth\":5,\"guards\":1}",
                Description =
                    "Derrotar o alvo protegido por poucos inimigos."
            },
            new Mission
            {
                Id = 2,
                Name = "Caça Média",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 2,
                ParametersJson =
                    "{\"targetHealth\":7,\"guards\":2}",
                Description =
                    "Derrotar um alvo mais resistente protegido por uma quantidade intermediária de inimigos."
            },
            new Mission
            {
                Id = 3,
                Name = "Caça Elite",
                Type = MissionType.Combat,
                Template = "Eliminar Alvo",
                Difficulty = 3,
                ParametersJson =
                    "{\"targetHealth\":10,\"guards\":3}",
                Description =
                    "Derrotar um alvo de alta resistência protegido por vários inimigos."
            },

            /*
             * COMBATE — SOBREVIVER
             */
            new Mission
            {
                Id = 4,
                Name = "Sobrevivência Inicial",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 1,
                ParametersJson =
                    "{\"waves\":3}",
                Description =
                    "Sobreviver e eliminar três ondas de inimigos."
            },
            new Mission
            {
                Id = 5,
                Name = "Sobrevivência Intermediária",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 2,
                ParametersJson =
                    "{\"waves\":5}",
                Description =
                    "Sobreviver e eliminar cinco ondas de dificuldade progressiva."
            },
            new Mission
            {
                Id = 6,
                Name = "Sobrevivência Extrema",
                Type = MissionType.Combat,
                Template = "Sobreviver",
                Difficulty = 3,
                ParametersJson =
                    "{\"waves\":7}",
                Description =
                    "Sobreviver e eliminar sete ondas de inimigos sob alta pressão."
            },

            /*
             * COMBATE — DEFENDER OBJETO
             */
            new Mission
            {
                Id = 7,
                Name = "Defesa Básica",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 1,
                ParametersJson =
                    "{\"waves\":2,\"objectHealth\":14}",
                Description =
                    "Defender o cristal durante poucas ondas, adaptando-se aos reposicionamentos após dano."
            },
            new Mission
            {
                Id = 23,
                Name = "Defesa Intermediária",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 2,
                ParametersJson =
                    "{\"waves\":3,\"objectHealth\":12}",
                Description =
                    "Defender o cristal durante uma quantidade intermediária de ondas, adaptando-se aos reposicionamentos após dano."
            },
            new Mission
            {
                Id = 8,
                Name = "Defesa Avançada",
                Type = MissionType.Combat,
                Template = "Defender Objeto",
                Difficulty = 3,
                ParametersJson =
                    "{\"waves\":5,\"objectHealth\":10}",
                Description =
                    "Defender o cristal contra várias ondas, adaptando-se aos reposicionamentos após dano."
            },

            /*
             * EXPLORAÇÃO — ENCONTRAR OBJETOS
             */
            new Mission
            {
                Id = 9,
                Name = "Exploração Simples",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 1,
                ParametersJson =
                    "{\"items\":3,\"lightScale\":3.0}",
                Description =
                    "Explorar um labirinto simples com visibilidade limitada para encontrar poucos objetos."
            },
            new Mission
            {
                Id = 10,
                Name = "Exploração Média",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 2,
                ParametersJson =
                    "{\"items\":6,\"lightScale\":2.35}",
                Description =
                    "Explorar um labirinto intermediário com visibilidade reduzida para encontrar objetos distribuídos pelo cenário."
            },
            new Mission
            {
                Id = 11,
                Name = "Exploração Difícil",
                Type = MissionType.Exploration,
                Template = "Encontrar Objetos",
                Difficulty = 3,
                ParametersJson =
                    "{\"items\":10,\"lightScale\":1.75}",
                Description =
                    "Explorar um labirinto complexo com baixa visibilidade para encontrar todos os objetos."
            },

            /*
             * EXPLORAÇÃO — CHEGAR AO DESTINO
             */
            new Mission
            {
                Id = 12,
                Name = "Navegação Simples",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 1,
                ParametersJson =
                    "{\"checkpoints\":2,\"hazards\":2,\"lasers\":1,\"maxFailures\":4,\"safeSeconds\":2.8,\"warningSeconds\":1.15,\"activeSeconds\":1.0}",
                Description =
                    "Percorrer uma rota curta seguindo checkpoints e atravessando poucos obstáculos temporizados."
            },
            new Mission
            {
                Id = 24,
                Name = "Navegação Intermediária",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 2,
                ParametersJson =
                    "{\"checkpoints\":3,\"hazards\":4,\"lasers\":3,\"maxFailures\":3,\"safeSeconds\":2.0,\"warningSeconds\":0.8,\"activeSeconds\":1.25}",
                Description =
                    "Percorrer uma rota intermediária seguindo checkpoints e sincronizando a travessia de obstáculos temporizados."
            },
            new Mission
            {
                Id = 13,
                Name = "Navegação Complexa",
                Type = MissionType.Exploration,
                Template = "Chegar ao Destino",
                Difficulty = 3,
                ParametersJson =
                    "{\"checkpoints\":5,\"hazards\":6,\"lasers\":5,\"maxFailures\":2,\"safeSeconds\":1.35,\"warningSeconds\":0.55,\"activeSeconds\":1.55}",
                Description =
                    "Percorrer uma rota complexa com vários checkpoints, lasers e áreas energizadas de janelas reduzidas."
            },

            /*
             * EXPLORAÇÃO — EVITAR INIMIGOS
             */
            new Mission
            {
                Id = 14,
                Name = "Furtividade Básica",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 1,
                ParametersJson =
                    "{\"enemies\":2,\"enemySpeed\":65,\"detectionRadius\":70,\"coneAngle\":52,\"suspicionSeconds\":1.35,\"safePoints\":1,\"maxFailures\":4}",
                Description =
                    "Atravessar uma rota de furtividade com poucas patrulhas, usando cobertura e pontos seguros."
            },
            new Mission
            {
                Id = 25,
                Name = "Furtividade Intermediária",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 2,
                ParametersJson =
                    "{\"enemies\":4,\"enemySpeed\":85,\"detectionRadius\":90,\"coneAngle\":68,\"suspicionSeconds\":0.9,\"safePoints\":2,\"maxFailures\":3}",
                Description =
                    "Atravessar uma rota de furtividade intermediária com múltiplas patrulhas, cobertura e pontos seguros."
            },
            new Mission
            {
                Id = 15,
                Name = "Furtividade Avançada",
                Type = MissionType.Exploration,
                Template = "Evitar Inimigos",
                Difficulty = 3,
                ParametersJson =
                    "{\"enemies\":6,\"enemySpeed\":110,\"detectionRadius\":115,\"coneAngle\":88,\"suspicionSeconds\":0.55,\"safePoints\":3,\"maxFailures\":2}",
                Description =
                    "Atravessar uma rota de furtividade avançada com várias patrulhas, cones de visão amplos e menor tempo de reação."
            },

            /*
             * QUEBRA-CABEÇA — REPETIR SEQUÊNCIA
             */
            new Mission
            {
                Id = 16,
                Name = "Sequência Simples",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 1,
                ParametersJson =
                    "{\"sequenceSize\":3,\"maxFailures\":4}",
                Description =
                    "Repetir uma sequência curta de símbolos."
            },
            new Mission
            {
                Id = 17,
                Name = "Sequência Média",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 2,
                ParametersJson =
                    "{\"sequenceSize\":5,\"maxFailures\":3}",
                Description =
                    "Repetir uma sequência de tamanho intermediário."
            },
            new Mission
            {
                Id = 18,
                Name = "Sequência Difícil",
                Type = MissionType.Puzzle,
                Template = "Repetir Sequência",
                Difficulty = 3,
                ParametersJson =
                    "{\"sequenceSize\":8,\"maxFailures\":2}",
                Description =
                    "Repetir uma sequência longa de alta exigência de memória."
            },

            /*
             * QUEBRA-CABEÇA — CONECTAR PONTOS
             */
            new Mission
            {
                Id = 19,
                Name = "Conexão Básica",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 1,
                ParametersJson =
                    "{\"pieces\":4,\"maxFailures\":5}",
                Description =
                    "Conectar poucos pares de pontos respeitando as regras do tabuleiro."
            },
            new Mission
            {
                Id = 26,
                Name = "Conexão Intermediária",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 2,
                ParametersJson =
                    "{\"pieces\":6,\"maxFailures\":4}",
                Description =
                    "Conectar uma quantidade intermediária de pares de pontos respeitando as regras do tabuleiro."
            },
            new Mission
            {
                Id = 20,
                Name = "Conexão Avançada",
                Type = MissionType.Puzzle,
                Template = "Conectar Pontos",
                Difficulty = 3,
                ParametersJson =
                    "{\"pieces\":8,\"maxFailures\":3}",
                Description =
                    "Conectar vários pares de pontos em um tabuleiro de maior complexidade."
            },

            /*
             * QUEBRA-CABEÇA — DECIFRAR CÓDIGO
             */
            new Mission
            {
                Id = 21,
                Name = "Código Simples",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 1,
                ParametersJson =
                    "{\"boards\":1,\"attempts\":6}",
                Description =
                    "Descobrir uma sequência oculta de cinco símbolos em até seis tentativas."
            },
            new Mission
            {
                Id = 27,
                Name = "Código Intermediário",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 2,
                ParametersJson =
                    "{\"boards\":2,\"attempts\":7}",
                Description =
                    "Descobrir duas sequências ocultas simultaneamente em até sete tentativas."
            },
            new Mission
            {
                Id = 22,
                Name = "Código Difícil",
                Type = MissionType.Puzzle,
                Template = "Decifrar Código",
                Difficulty = 3,
                ParametersJson =
                    "{\"boards\":4,\"attempts\":9}",
                Description =
                    "Descobrir quatro sequências ocultas simultaneamente em até nove tentativas."
            }
        );
    }
}