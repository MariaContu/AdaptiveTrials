using AdaptiveTrials.Game.Missions.Shared;
using Godot;

namespace AdaptiveTrials.Game.Tests;

/// <summary>
/// Valida a progressão sequencial de checkpoints sem sessão ou API.
/// </summary>
public partial class ExplorationCheckpointTestController : Node
{
	private CheckpointArea[] _checkpoints = null!;
	private Label _statusLabel = null!;
	private int _reached;

	public override void _Ready()
	{
		_statusLabel =
			GetNode<Label>(
				"../Interface/StatusLabel");

		_checkpoints =
			new[]
			{
				GetNode<CheckpointArea>("../Checkpoint01"),
				GetNode<CheckpointArea>("../Checkpoint02"),
				GetNode<CheckpointArea>("../Checkpoint03")
			};

		for (int index = 0;
			 index < _checkpoints.Length;
			 index++)
		{
			int checkpointNumber =
				index + 1;

			_checkpoints[index]
				.ConfigureIndex(checkpointNumber);

			_checkpoints[index]
				.SetEnabledState(index == 0);

			_checkpoints[index]
				.CheckpointReached +=
					OnCheckpointReached;
		}

		UpdateStatus();
	}

	private void OnCheckpointReached(
		CheckpointArea checkpoint)
	{
		_reached++;

		if (_reached < _checkpoints.Length)
		{
			_checkpoints[_reached]
				.SetEnabledState(true);
		}

		UpdateStatus();
	}

	private void UpdateStatus()
	{
		_statusLabel.Text =
			_reached >= _checkpoints.Length
				? "ROTA CONCLUÍDA — progressão sequencial funcionando."
				: $"Alcance o ponto {_reached + 1}. " +
				  "Os próximos devem permanecer bloqueados.";
	}
}
