using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdaptiveTrials.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Missions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Template = table.Column<string>(type: "TEXT", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Missions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SteamId = table.Column<string>(type: "TEXT", nullable: true),
                    ManualPreferencesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Combat = table.Column<double>(type: "REAL", nullable: false),
                    Exploration = table.Column<double>(type: "REAL", nullable: false),
                    Puzzle = table.Column<double>(type: "REAL", nullable: false),
                    TotalPlaytime = table.Column<double>(type: "REAL", nullable: false),
                    NumGames = table.Column<int>(type: "INTEGER", nullable: false),
                    GamesCombat = table.Column<int>(type: "INTEGER", nullable: false),
                    GamesExploration = table.Column<int>(type: "INTEGER", nullable: false),
                    GamesPuzzle = table.Column<int>(type: "INTEGER", nullable: false),
                    HoursCombat = table.Column<double>(type: "REAL", nullable: false),
                    HoursExploration = table.Column<double>(type: "REAL", nullable: false),
                    HoursPuzzle = table.Column<double>(type: "REAL", nullable: false),
                    AvgPlaytimePerGame = table.Column<double>(type: "REAL", nullable: true),
                    Diversity = table.Column<double>(type: "REAL", nullable: true),
                    Entropy = table.Column<double>(type: "REAL", nullable: true),
                    Dominance = table.Column<double>(type: "REAL", nullable: true),
                    SecondMax = table.Column<double>(type: "REAL", nullable: true),
                    Gap = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedProfiles_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BehaviorEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionId = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Template = table.Column<string>(type: "TEXT", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionTime = table.Column<double>(type: "REAL", nullable: false),
                    Failures = table.Column<int>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Persistence = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BehaviorEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BehaviorEvents_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BehaviorEvents_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionId = table.Column<int>(type: "INTEGER", nullable: true),
                    RecommendedType = table.Column<int>(type: "INTEGER", nullable: false),
                    RecommendedDifficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    CombatProbability = table.Column<double>(type: "REAL", nullable: false),
                    ExplorationProbability = table.Column<double>(type: "REAL", nullable: false),
                    PuzzleProbability = table.Column<double>(type: "REAL", nullable: false),
                    ProfileWeight = table.Column<double>(type: "REAL", nullable: false),
                    BehaviorWeight = table.Column<double>(type: "REAL", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recommendations_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Recommendations_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BehaviorEvents_MissionId",
                table: "BehaviorEvents",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_BehaviorEvents_SessionId",
                table: "BehaviorEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedProfiles_PlayerId",
                table: "NormalizedProfiles",
                column: "PlayerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_MissionId",
                table: "Recommendations",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_SessionId",
                table: "Recommendations",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PlayerId",
                table: "Sessions",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BehaviorEvents");

            migrationBuilder.DropTable(
                name: "NormalizedProfiles");

            migrationBuilder.DropTable(
                name: "Recommendations");

            migrationBuilder.DropTable(
                name: "Missions");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
