using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Api;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Dto.Sessions;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Shared;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Mantém o estado da sessão durante a troca de cenas.
/// </summary>
public partial class SessionManager : Node
{
	private readonly List<MissionDto> _missionSequence =
		new();

	private readonly HashSet<int> _registeredMissionIndexes =
		new();

	private ApiClient _apiClient = null!;

	private bool _isRegisteringMissionResult;

	public int? SessionId { get; private set; }

	public int? PlayerId { get; private set; }

	public GameMode? Mode { get; private set; }

	public int CurrentMissionIndex { get; private set; }

	public int CompletedMissionCount { get; private set; }

	public int TotalMissionCount =>
		_missionSequence.Count;

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
		CurrentMissionIndex ==
		_missionSequence.Count - 1;

	public bool IsRegisteringMissionResult =>
		_isRegisteringMissionResult;

	public MissionDto? CurrentMission =>
		HasCurrentMission
			? _missionSequence[CurrentMissionIndex]
			: null;

	public override void _Ready()
	{
		_apiClient =
			GetNode<ApiClient>(
				"/root/ApiClient");
	}

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
		ArgumentNullException.ThrowIfNull(
			missions);

		if (missions.Count == 0)
		{
			throw new ArgumentException(
				"A sequência não pode estar vazia.",
				nameof(missions));
		}

		_missionSequence.Clear();
		_missionSequence.AddRange(missions);

		_registeredMissionIndexes.Clear();

		CurrentMissionIndex = 0;
		CompletedMissionCount = 0;

		GD.Print(
			$"Sequência armazenada no SessionManager: " +
			$"{_missionSequence.Count} missões.");
	}

	/// <summary>
	/// Envia o resultado da missão atual para a API.
	/// </summary>
	public async Task<bool> RegisterCurrentMissionResultAsync(
		MissionResult missionResult)
	{
		ArgumentNullException.ThrowIfNull(
			missionResult);

		if (!HasActiveSession ||
			!SessionId.HasValue)
		{
			GD.PushError(
				"Não existe uma sessão ativa para " +
				"registrar o evento.");

			return false;
		}

		if (!HasCurrentMission)
		{
			GD.PushError(
				"Não existe uma missão atual para " +
				"registrar o evento.");

			return false;
		}

		if (_registeredMissionIndexes.Contains(
				CurrentMissionIndex))
		{
			GD.PushWarning(
				"O resultado desta missão já foi registrado.");

			return true;
		}

		if (_isRegisteringMissionResult)
		{
			GD.PushWarning(
				"Já existe um evento comportamental " +
				"sendo registrado.");

			return false;
		}

		if (missionResult.MissionId !=
			CurrentMission!.Id)
		{
			GD.PushError(
				"O resultado recebido não pertence à " +
				"missão atual.");

			return false;
		}

		_isRegisteringMissionResult = true;

		try
		{
			RegisterSessionEventRequest request =
				new()
				{
					MissionId =
						missionResult.MissionId,

					CompletionTime =
						Math.Max(
							0,
							missionResult.CompletionTime),

					Failures =
						Math.Max(
							0,
							missionResult.Failures),

					Success =
						missionResult.Success,

					Persistence =
						Math.Clamp(
							missionResult.Persistence,
							0,
							1)
				};

			ApiResult<bool> apiResult =
				await _apiClient
					.RegisterSessionEventAsync(
						SessionId.Value,
						request);

			if (!apiResult.IsSuccess)
			{
				GD.PushError(
					$"Não foi possível registrar o " +
					$"evento comportamental. " +
					$"{apiResult.ErrorMessage}");

				return false;
			}

			_registeredMissionIndexes.Add(
				CurrentMissionIndex);

			MarkCurrentMissionCompleted();

			GD.Print(
				$"Evento comportamental registrado: " +
				$"SessionId={SessionId.Value}, " +
				$"MissionId={missionResult.MissionId}, " +
				$"Success={missionResult.Success}, " +
				$"Failures={missionResult.Failures}, " +
				$"Persistence=" +
				$"{missionResult.Persistence:F2}");

			return true;
		}
		catch (Exception exception)
		{
			GD.PushError(
				"Erro inesperado ao registrar o evento " +
				$"comportamental: {exception}");

			return false;
		}
		finally
		{
			_isRegisteringMissionResult = false;
		}
	}

	public bool TryAdvanceToNextMission()
	{
		if (!HasCurrentMission)
		{
			return false;
		}

		if (!_registeredMissionIndexes.Contains(
				CurrentMissionIndex))
		{
			GD.PushWarning(
				"A missão atual ainda não possui um " +
				"evento comportamental registrado.");

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
			$"{CompletedMissionCount}/" +
			$"{TotalMissionCount}");
	}

	public void ClearSession()
	{
		SessionId = null;
		PlayerId = null;
		Mode = null;

		ClearMissionProgress();

		GD.Print(
			"Estado da sessão removido.");
	}

	private void ClearMissionProgress()
	{
		_missionSequence.Clear();
		_registeredMissionIndexes.Clear();

		CurrentMissionIndex = 0;
		CompletedMissionCount = 0;

		_isRegisteringMissionResult = false;
	}
}
