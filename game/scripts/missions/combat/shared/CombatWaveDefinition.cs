namespace AdaptiveTrials.Game.Missions.Combat.Shared;

/// <summary>
/// Define a composição e os atributos de uma onda de combate.
/// </summary>
public sealed class CombatWaveDefinition
{
    public int MeleeCount { get; set; }

    public int RangedCount { get; set; }

    public int EnemyHealth { get; set; } = 2;

    public int EnemyDamage { get; set; } = 1;

    public float MeleeSpeed { get; set; } = 75.0f;

    public float MeleeCooldownSeconds { get; set; } = 1.2f;

    public float RangedCooldownSeconds { get; set; } = 2.0f;

    public float RangedOrbSpeed { get; set; } = 300.0f;

    public bool RangedMovementEnabled { get; set; }

    public int TotalEnemies => MeleeCount + RangedCount;
}
