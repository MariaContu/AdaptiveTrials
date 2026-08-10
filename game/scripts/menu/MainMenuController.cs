#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Api;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Dto.Sessions;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Missions.Providers;
using AdaptiveTrials.Game.Session;
using Godot;

namespace AdaptiveTrials.Game.Menu;

/// <summary>
/// Controla a escolha do modo, a criação da sessão
/// e a preparação inicial do fluxo experimental.
/// </summary>
public partial class MainMenuController : Node
{
	private const string MissionTransitionScenePath =
		"res://scenes/missions/MissionTransition.tscn";

	private Button _settingsButton = null!;
	private Button _exitButton = null!;
	private Button _controlModeButton = null!;
	private Button _adaptiveModeButton = null!;
	private Button _startButton = null!;
	private Label _statusLabel = null!;

	private GameMode? _selectedMode;
	private bool _isCreatingSession;

	public override void _Ready()
	{
		GetInterfaceNodes();
		ConnectSignals();
		ResetInterface();
	}

	private void GetInterfaceNodes()
	{
		_settingsButton =
			GetNode<Button>(
				"../InterfaceMargin/InterfaceRoot/" +
				"TopBar/SettingsButton");

		_exitButton =
			GetNode<Button>(
				"../InterfaceMargin/InterfaceRoot/" +
				"TopBar/ExitButton");

		_controlModeButton =
			GetNode<Button>(
				"../InterfaceMargin/InterfaceRoot/" +
				"ModeCardsCenter/ModeCards/" +
				"ControlModeButton");

		_adaptiveModeButton =
			GetNode<Button>(
				"../InterfaceMargin/InterfaceRoot/" +
				"ModeCardsCenter/ModeCards/" +
				"AdaptiveModeButton");

		_startButton =
			GetNode<Button>(
				"../InterfaceMargin/InterfaceRoot/" +
				"StartCenter/StartButton");

		_statusLabel =
			GetNode<Label>(
				"../InterfaceMargin/InterfaceRoot/" +
				"StatusLabel");
	}

	private void ConnectSignals()
	{
		_settingsButton.Pressed += OnSettingsPressed;
		_exitButton.Pressed += OnExitPressed;

		_controlModeButton.Pressed +=
			OnControlModePressed;

		_adaptiveModeButton.Pressed +=
			OnAdaptiveModePressed;

		_startButton.Pressed += OnStartPressed;
	}

	private void ResetInterface()
	{
		_selectedMode = null;
		_isCreatingSession = false;

		_controlModeButton.ButtonPressed = false;
		_adaptiveModeButton.ButtonPressed = false;

		_controlModeButton.Disabled = false;
		_adaptiveModeButton.Disabled = false;

		_startButton.Disabled = true;

		_statusLabel.Text = string.Empty;
	}

	private void OnControlModePressed()
	{
		SelectMode(GameMode.Control);
	}

	private void OnAdaptiveModePressed()
	{
		SelectMode(GameMode.Adaptive);
	}

	private void SelectMode(GameMode mode)
	{
		if (_isCreatingSession)
		{
			return;
		}

		_selectedMode = mode;

		bool controlSelected =
			mode == GameMode.Control;

		_controlModeButton.ButtonPressed =
			controlSelected;

		_adaptiveModeButton.ButtonPressed =
			!controlSelected;

		_startButton.Disabled = false;

		_statusLabel.Text = controlSelected
			? "Modo Controle selecionado."
			: "Modo Adaptativo selecionado.";

		GD.Print(
			$"Modo selecionado: {mode} ({(int)mode})");
	}

	private void OnStartPressed()
	{
		if (_isCreatingSession)
		{
			return;
		}

		_ = CreateSessionAsync();
	}

	private async Task CreateSessionAsync()
	{
		if (_selectedMode is null)
		{
			_statusLabel.Text =
				"Escolha um modo antes de iniciar.";

			return;
		}

		SetLoadingState(true, "Criando sessão...");

		try
		{
			ApiClient apiClient =
				GetNode<ApiClient>("/root/ApiClient");

			SessionManager sessionManager =
				GetNode<SessionManager>(
					"/root/SessionManager");

			// Garante que uma nova execução não reutilize
			// informações da sessão anterior.
			sessionManager.ClearSession();

			CreateSessionRequest request = new()
			{
				PlayerId = null,
				Mode = _selectedMode.Value
			};

			ApiResult<CreateSessionResponse> result =
				await apiClient.CreateSessionAsync(request);

			if (!IsInstanceValid(this))
			{
				return;
			}

			if (!result.IsSuccess || result.Data is null)
			{
				ShowRequestError(
					"Não foi possível criar a sessão.",
					result.ErrorMessage);

				return;
			}

			CreateSessionResponse createdSession =
				result.Data;

			sessionManager.InitializeSession(
				createdSession.SessionId,
				createdSession.PlayerId,
				createdSession.Mode);

			GD.Print(
				$"Sessão criada pelo menu: " +
				$"SessionId={createdSession.SessionId}, " +
				$"PlayerId={createdSession.PlayerId}, " +
				$"Mode={createdSession.Mode} " +
				$"({(int)createdSession.Mode})");

			await ContinueAfterSessionCreationAsync(
				apiClient,
				sessionManager,
				createdSession.Mode);
		}
		catch (Exception exception)
		{
			GD.PushError(
				$"Erro inesperado ao criar a sessão: " +
				$"{exception}");

			if (IsInstanceValid(this))
			{
				ShowRequestError(
					"Ocorreu um erro inesperado ao criar a sessão.",
					exception.Message);
			}
		}
	}

	private async Task ContinueAfterSessionCreationAsync(
		ApiClient apiClient,
		SessionManager sessionManager,
		GameMode mode)
	{
		switch (mode)
		{
			case GameMode.Control:
				await StartControlFlowAsync(
					apiClient,
					sessionManager);
				break;

			case GameMode.Adaptive:
				StartAdaptiveFlow();
				break;

			default:
				ShowRequestError(
					"O modo de jogo selecionado é inválido.",
					$"Mode received: {(int)mode}");
				break;
		}
	}

	private async Task StartControlFlowAsync(
		ApiClient apiClient,
		SessionManager sessionManager)
	{
		bool sequencePrepared =
			await PrepareControlSessionAsync(
				apiClient,
				sessionManager);

		if (!sequencePrepared)
		{
			SetLoadingState(false);
			return;
		}

		NavigateToMissionTransition();
	}

	private async Task<bool> PrepareControlSessionAsync(
		ApiClient apiClient,
		SessionManager sessionManager)
	{
		SetLoadingState(
			true,
			"Preparando missões do modo Controle...");

		ApiResult<IReadOnlyList<MissionDto>>
			catalogResult =
				await apiClient.GetMissionsAsync();

		if (!IsInstanceValid(this))
		{
			return false;
		}

		if (!catalogResult.IsSuccess ||
			catalogResult.Data is null)
		{
			ShowRequestError(
				"Não foi possível carregar o catálogo de missões.",
				catalogResult.ErrorMessage);

			return false;
		}

		try
		{
			ControlMissionProvider provider = new();

			IReadOnlyList<MissionDto> sequence =
				provider.BuildSequence(
					catalogResult.Data);

			sessionManager.SetMissionSequence(sequence);

			GD.Print(
				$"Modo controle preparado com " +
				$"{sequence.Count} missões.");

			return true;
		}
		catch (Exception exception)
		{
			GD.PushError(
				$"Erro ao preparar a sequência " +
				$"do modo controle: {exception}");

			ShowRequestError(
				"Não foi possível preparar a sequência de missões do modo Controle.",
				exception.Message);

			return false;
		}
	}

	private void NavigateToMissionTransition()
	{
		SetLoadingState(
			true,
			"Abrindo a primeira missão...");

		Error navigationError =
			GetTree().ChangeSceneToFile(
				MissionTransitionScenePath);

		if (navigationError == Error.Ok)
		{
			return;
		}

		GD.PushError(
			$"Não foi possível abrir a cena " +
			$"{MissionTransitionScenePath}. " +
			$"Erro: {navigationError}");

		ShowRequestError(
			"Não foi possível abrir a tela da missão.",
			navigationError.ToString());
	}

	private void StartAdaptiveFlow()
	{
		/*
		 * O fluxo adaptativo será conectado depois:
		 *
		 * 1. tela de Steam ID;
		 * 2. possibilidade de pular a Steam;
		 * 3. preferências manuais;
		 * 4. recomendação pela API.
		 */

		_statusLabel.Text =
			"A configuração do perfil adaptativo será implementada na próxima etapa.";

		SetLoadingState(false);

		GD.Print(
			"Sessão adaptativa criada. " +
			"Aguardando implementação do fluxo de perfil.");
	}

	private void SetLoadingState(
		bool loading,
		string? message = null)
	{
		_isCreatingSession = loading;

		_controlModeButton.Disabled = loading;
		_adaptiveModeButton.Disabled = loading;

		_startButton.Disabled =
			loading || _selectedMode is null;

		if (!string.IsNullOrWhiteSpace(message))
		{
			_statusLabel.Text = message;
		}
	}

	private void ShowRequestError(
		string title,
		string details)
	{
		_statusLabel.Text =
			string.IsNullOrWhiteSpace(details)
				? title
				: $"{title}\n{details}";

		SetLoadingState(false);
	}

	private void OnSettingsPressed()
	{
		if (_isCreatingSession)
		{
			return;
		}

		_statusLabel.Text =
			"As configurações serão implementadas posteriormente.";
	}

	private void OnExitPressed()
	{
		if (_isCreatingSession)
		{
			return;
		}

		GetTree().Quit();
	}
}
