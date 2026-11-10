using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Api;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Dto.Sessions;
using AdaptiveTrials.Game.Dto.Adaptive;
using AdaptiveTrials.Game.Missions.Providers;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Shared;
using Godot;

namespace AdaptiveTrials.Game.Session;

/// <summary>
/// Mantém o estado e controla a progressão
/// da sessão experimental.
/// </summary>
public partial class SessionManager : Node
{
	public const string MissionTransitionScenePath =
		"res://scenes/missions/MissionTransition.tscn";

	public const string SessionSummaryScenePath =
		"res://scenes/session/SessionSummary.tscn";


	private readonly List<MissionDto> _missionSequence =
		new();

	private readonly List<CompletedMissionRecord>
		_completedMissionRecords = new();

	private readonly HashSet<int> _registeredMissionIndexes =
		new();

	private ApiClient _apiClient = null!;

	private NextRecommendationResponse? _pendingAdaptiveRecommendation;
	private bool _isPreparingAdaptiveMission;
	private bool _isRegisteringMissionResult;
	private bool _isContinuingSession;
	private bool _isEndingSession;
	private bool _sessionEnded;

	public int? SessionId { get; private set; }

	public int? PlayerId { get; private set; }

	public GameMode? Mode { get; private set; }

	public int CurrentMissionIndex { get; private set; }

	public int CompletedMissionCount =>
		_completedMissionRecords.Count;

	public int TotalMissionCount =>
		Mode == GameMode.Adaptive
			? AdaptiveMissionPolicy.MissionLimit
			: _missionSequence.Count;

	public IReadOnlyList<MissionDto> MissionSequence =>
		_missionSequence;

	public IReadOnlyList<CompletedMissionRecord>
		CompletedMissionRecords =>
			_completedMissionRecords;

	public bool HasActiveSession =>
		SessionId.HasValue &&
		PlayerId.HasValue &&
		Mode.HasValue &&
		!_sessionEnded;

	public bool HasSessionIdentifiers =>
		SessionId.HasValue &&
		PlayerId.HasValue;

	public bool HasMissionSequence =>
		_missionSequence.Count > 0;

	public bool HasCurrentMission =>
		CurrentMissionIndex >= 0 &&
		CurrentMissionIndex <
		_missionSequence.Count;

	public bool IsLastMission =>
		HasCurrentMission &&
		CurrentMissionIndex ==
		TotalMissionCount - 1;

	public bool IsRegisteringMissionResult =>
		_isRegisteringMissionResult;

	public bool IsContinuingSession =>
		_isContinuingSession;

	public bool IsEndingSession =>
		_isEndingSession;

	public bool SessionEnded =>
		_sessionEnded;

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

		_sessionEnded = false;
		_isEndingSession = false;
		_isContinuingSession = false;

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

		if (Mode == GameMode.Adaptive)
		{
			throw new InvalidOperationException(
				"O modo Adaptativo prepara as missões individualmente.");
		}

		if (missions.Count == 0)
		{
			throw new ArgumentException(
				"A sequência não pode estar vazia.",
				nameof(missions));
		}

		_missionSequence.Clear();
		_missionSequence.AddRange(missions);

		_completedMissionRecords.Clear();
		_registeredMissionIndexes.Clear();

		CurrentMissionIndex = 0;

		GD.Print(
			$"Sequência armazenada no SessionManager: " +
			$"{_missionSequence.Count} missões.");
	}

	/// <summary>
	/// Carrega uma recomendação de cada vez, sempre após o evento anterior.
	/// Preserva a recomendação recebida se a consulta ao catálogo falhar,
	/// evitando gerar outra recomendação em uma simples tentativa de repetição.
	/// </summary>
	public async Task<bool> PrepareNextAdaptiveMissionAsync()
	{
		if (!HasActiveSession || Mode != GameMode.Adaptive ||
			!SessionId.HasValue || !PlayerId.HasValue ||
			_isPreparingAdaptiveMission)
		{
			return false;
		}

		if (_missionSequence.Count >= AdaptiveMissionPolicy.MissionLimit ||
			CompletedMissionCount != _missionSequence.Count)
		{
			GD.PushWarning("A próxima missão adaptativa ainda não pode ser preparada.");
			return false;
		}

		_isPreparingAdaptiveMission = true;
		try
		{
			if (_pendingAdaptiveRecommendation is null)
			{
				ApiResult<NextRecommendationResponse> recommendationResult =
					await _apiClient.GetNextRecommendationAsync(
						PlayerId.Value, SessionId.Value);

				if (!recommendationResult.IsSuccess || recommendationResult.Data is null)
				{
					GD.PushError("Não foi possível obter a recomendação adaptativa: " +
						recommendationResult.ErrorMessage);
					return false;
				}

				_pendingAdaptiveRecommendation = recommendationResult.Data;
			}

			ApiResult<MissionDto> missionResult =
				await _apiClient.GetMissionByIdAsync(
					_pendingAdaptiveRecommendation.RecommendedMissionId);

			if (!missionResult.IsSuccess || missionResult.Data is null)
			{
				GD.PushError("Não foi possível carregar a missão recomendada: " +
					missionResult.ErrorMessage);
				return false;
			}

			AdaptiveMissionPolicy.ValidateNext(
				_missionSequence,
				CompletedMissionCount,
				_pendingAdaptiveRecommendation,
				missionResult.Data);

			_missionSequence.Add(missionResult.Data);
			GD.Print($"Missão adaptativa preparada: " +
				$"{_missionSequence.Count}/{TotalMissionCount}, " +
				$"MissionId={missionResult.Data.Id}, " +
				$"Dificuldade={missionResult.Data.Difficulty}, " +
				$"Recomendação={_pendingAdaptiveRecommendation.RecommendationId}");
			_pendingAdaptiveRecommendation = null;
			return true;
		}
		catch (Exception exception)
		{
			GD.PushError("Erro ao preparar a missão adaptativa: " + exception);
			return false;
		}
		finally
		{
			_isPreparingAdaptiveMission = false;
		}
	}

	/// <summary>
	/// Envia o resultado da missão atual para a API
	/// e o armazena localmente.
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

		MissionDto mission =
			CurrentMission!;

		if (missionResult.MissionId !=
			mission.Id)
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

			_completedMissionRecords.Add(
				new CompletedMissionRecord
				{
					Mission = mission,
					Result = missionResult
				});

			GD.Print(
				$"Evento comportamental registrado: " +
				$"SessionId={SessionId.Value}, " +
				$"MissionId={missionResult.MissionId}, " +
				$"Success={missionResult.Success}, " +
				$"Failures={missionResult.Failures}, " +
				$"Persistence=" +
				$"{missionResult.Persistence:F2}");

			GD.Print(
				$"Progresso da sessão: " +
				$"{CompletedMissionCount}/" +
				$"{TotalMissionCount}");

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

	/// <summary>
	/// Avança para a próxima missão ou encerra a sessão
	/// depois da última atividade.
	/// </summary>
	public async Task<bool> ContinueAfterCurrentMissionAsync(
		SceneTree sceneTree)
	{
		ArgumentNullException.ThrowIfNull(
			sceneTree);

		if (_isContinuingSession)
		{
			GD.PushWarning(
				"A continuação da sessão já está em andamento.");

			return false;
		}

		if (!HasActiveSession)
		{
			GD.PushError(
				"Não existe uma sessão ativa.");

			return false;
		}

		if (!HasCurrentMission)
		{
			GD.PushError(
				"Não existe uma missão atual.");

			return false;
		}

		if (!_registeredMissionIndexes.Contains(
				CurrentMissionIndex))
		{
			GD.PushError(
				"A missão atual ainda não possui um " +
				"evento comportamental registrado.");

			return false;
		}

		_isContinuingSession = true;

		try
		{
			if (IsLastMission)
			{
				bool sessionEnded =
					await EndCurrentSessionAsync();

				if (!sessionEnded)
				{
					return false;
				}

				return ChangeScene(
					sceneTree,
					SessionSummaryScenePath);
			}

			if (Mode == GameMode.Adaptive &&
				!await PrepareNextAdaptiveMissionAsync())
			{
				return false;
			}

			CurrentMissionIndex++;

			GD.Print(
				$"Avançando para a missão " +
				$"{CurrentMissionIndex + 1}/" +
				$"{TotalMissionCount}: " +
				$"{CurrentMission?.Name}");

			return ChangeScene(
				sceneTree,
				MissionTransitionScenePath);
		}
		finally
		{
			_isContinuingSession = false;
		}
	}

	/// <summary>
	/// Encerra a sessão atual no backend.
	/// </summary>
	public async Task<bool> EndCurrentSessionAsync()
	{
		if (!SessionId.HasValue)
		{
			GD.PushError(
				"Não existe identificador de sessão.");

			return false;
		}

		if (_sessionEnded)
		{
			return true;
		}

		if (_isEndingSession)
		{
			GD.PushWarning(
				"O encerramento da sessão já está " +
				"em andamento.");

			return false;
		}

		if (CompletedMissionCount <
			TotalMissionCount)
		{
			GD.PushError(
				"A sessão não pode ser encerrada antes " +
				"do registro de todas as missões.");

			return false;
		}

		_isEndingSession = true;

		try
		{
			ApiResult<bool> apiResult =
				await _apiClient.EndSessionAsync(
					SessionId.Value);

			if (!apiResult.IsSuccess)
			{
				GD.PushError(
					$"Não foi possível encerrar a sessão. " +
					$"{apiResult.ErrorMessage}");

				return false;
			}

			_sessionEnded = true;

			GD.Print(
				$"Sessão encerrada com sucesso: " +
				$"SessionId={SessionId.Value}, " +
				$"PlayerId={PlayerId}, " +
				$"CompletedMissions=" +
				$"{CompletedMissionCount}/" +
				$"{TotalMissionCount}");

			return true;
		}
		catch (Exception exception)
		{
			GD.PushError(
				"Erro inesperado ao encerrar a sessão: " +
				exception);

			return false;
		}
		finally
		{
			_isEndingSession = false;
		}
	}

	public void ClearSession()
	{
		SessionId = null;
		PlayerId = null;
		Mode = null;

		_sessionEnded = false;
		_isEndingSession = false;
		_isContinuingSession = false;

		ClearMissionProgress();

		GD.Print(
			"Estado da sessão removido.");
	}

	private static bool ChangeScene(
		SceneTree sceneTree,
		string scenePath)
	{
		if (!ResourceLoader.Exists(
				scenePath))
		{
			GD.PushError(
				$"A cena não foi encontrada: " +
				$"{scenePath}");

			return false;
		}

		Error navigationError =
			sceneTree.ChangeSceneToFile(
				scenePath);

		if (navigationError != Error.Ok)
		{
			GD.PushError(
				$"Não foi possível abrir a cena " +
				$"{scenePath}. Erro: " +
				$"{navigationError}");

			return false;
		}

		return true;
	}

	private void ClearMissionProgress()
	{
		_missionSequence.Clear();
		_completedMissionRecords.Clear();
		_registeredMissionIndexes.Clear();

		CurrentMissionIndex = 0;

		_pendingAdaptiveRecommendation = null;
		_isPreparingAdaptiveMission = false;
		_isRegisteringMissionResult = false;
	}
}
