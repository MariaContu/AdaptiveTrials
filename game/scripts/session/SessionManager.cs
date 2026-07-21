using System;
using System.Collections.Generic;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Enums;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Mantém o estado da sessão durante a troca de cenas.
/// </summary>
public partial class SessionManager : Node
{
	private readonly List<MissionDto> _missionSequence = new();

	public int? SessionId { get; private set; }

	public int? PlayerId { get; private set; }

	public GameMode? Mode { get; private set; }

	public int CurrentMissionIndex { get; private set; }

	public int CompletedMissionCount { get; private set; }

	public int TotalMissionCount => _missionSequence.Count;

	public IReadOnlyList<MissionDto> MissionSequence =>
		_missionSequence;

	public bool HasActiveSession =>
		SessionId.HasValue &&
		PlayerId.HasValue &&
		Mode.HasValue;

	public bool HasMissionSequence =>
		_missionSequence.Count > 0;

	public bool HasCurrentMission =>
		CurrentMissionIndex >= 0 &&
		CurrentMissionIndex < _missionSequence.Count;

	public bool IsLastMission =>
		HasCurrentMission &&
		CurrentMissionIndex == _missionSequence.Count - 1;

	public MissionDto? CurrentMission =>
		HasCurrentMission
			? _missionSequence[CurrentMissionIndex]
			: null;

	public void InitializeSession(
		int sessionId,
		int playerId,
		GameMode mode)
	{
		ClearMissionProgress();

		SessionId = sessionId;
		PlayerId = playerId;
		Mode = mode;

		GD.Print(
			$"SessionManager inicializado: " +
			$"SessionId={SessionId}, " +
			$"PlayerId={PlayerId}, " +
			$"Mode={Mode}");
	}

	public void SetMissionSequence(
		IReadOnlyList<MissionDto> missions)
	{
		ArgumentNullException.ThrowIfNull(missions);

		if (missions.Count == 0)
		{
			throw new ArgumentException(
				"A sequência não pode estar vazia.",
				nameof(missions));
		}

		_missionSequence.Clear();
		_missionSequence.AddRange(missions);

		CurrentMissionIndex = 0;
		CompletedMissionCount = 0;

		GD.Print(
			$"Sequência armazenada no SessionManager: " +
			$"{_missionSequence.Count} missões.");
	}

	public bool TryAdvanceToNextMission()
	{
		if (!HasCurrentMission)
		{
			return false;
		}

		if (IsLastMission)
		{
			return false;
		}

		CurrentMissionIndex++;

		return true;
	}

	public void MarkCurrentMissionCompleted()
	{
		if (!HasCurrentMission)
		{
			throw new InvalidOperationException(
				"Não existe uma missão atual.");
		}

		CompletedMissionCount++;

		GD.Print(
			$"Missão concluída. Total: " +
			$"{CompletedMissionCount}/{TotalMissionCount}");
	}

	public void ClearSession()
	{
		SessionId = null;
		PlayerId = null;
		Mode = null;

		ClearMissionProgress();

		GD.Print("Estado da sessão removido.");
	}

	private void ClearMissionProgress()
	{
		_missionSequence.Clear();

		CurrentMissionIndex = 0;
		CompletedMissionCount = 0;
	}
}
