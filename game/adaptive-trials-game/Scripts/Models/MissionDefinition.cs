using Godot;

namespace AdaptiveTrials.Models;

public sealed class MissionDefinition
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required string Description { get; init; }

	public required MissionCategory Category { get; init; }

	public required int Difficulty { get; init; }

	public required string ScenePath { get; init; }

	public override string ToString()
	{
		return $"{Id} - {Name} ({Category}, dificuldade {Difficulty})";
	}

	public bool HasValidScenePath()
	{
		return !string.IsNullOrWhiteSpace(ScenePath)
			&& ResourceLoader.Exists(ScenePath);
	}
}
