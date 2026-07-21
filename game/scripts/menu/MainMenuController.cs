using AdaptiveTrials.Game.Api;
using AdaptiveTrials.Game.Dto.Sessions;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Session;
using Godot;
using System;
using System.Threading.Tasks;

namespace AdaptiveTrials.Game.Menu;

/// <summary>
/// Controla a escolha do modo e o início da sessão.
/// </summary>
public partial class MainMenuController : Node
{
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
				"ModeCardsCenter/ModeCards/ControlModeButton");

		_adaptiveModeButton =
			GetNode<Button>(
				"../InterfaceMargin/InterfaceRoot/" +
				"ModeCardsCenter/ModeCards/AdaptiveModeButton");

		_startButton =
			GetNode<Button>(
				"../InterfaceMargin/InterfaceRoot/" +
				"StartCenter/StartButton");

		_statusLabel =
			GetNode<Label>(
				"../InterfaceMargin/InterfaceRoot/StatusLabel");

		_settingsButton.Pressed += OnSettingsPressed;
		_exitButton.Pressed += OnExitPressed;
		_controlModeButton.Pressed += OnControlModePressed;
		_adaptiveModeButton.Pressed += OnAdaptiveModePressed;
		_startButton.Pressed += OnStartPressed;

		_statusLabel.Text = string.Empty;
		_startButton.Disabled = true;
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
		_startButton.Disabled = false;

		bool controlSelected =
			mode == GameMode.Control;

		_controlModeButton.ButtonPressed =
			controlSelected;

		_adaptiveModeButton.ButtonPressed =
			!controlSelected;

		_statusLabel.Text = controlSelected
			? "Control mode selected."
			: "Adaptive mode selected.";
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
				"Choose a mode before starting.";

			return;
		}

		SetLoadingState(true);

		try
		{
			ApiClient apiClient =
				GetNode<ApiClient>("/root/ApiClient");

			SessionManager sessionManager =
				GetNode<SessionManager>("/root/SessionManager");

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
				_statusLabel.Text =
					"The session could not be created.\n" +
					result.ErrorMessage;

				SetLoadingState(false);
				return;
			}

			sessionManager.InitializeSession(
				result.Data.SessionId,
				result.Data.PlayerId,
				result.Data.Mode);

			_statusLabel.Text =
				"Session created successfully.\n" +
				$"SessionId: {result.Data.SessionId}";

			GD.Print(
				$"Sessão criada pelo menu: " +
				$"SessionId={result.Data.SessionId}, " +
				$"PlayerId={result.Data.PlayerId}, " +
				$"Mode={result.Data.Mode} " +
				$"({(int)result.Data.Mode})");

			/*
			 * Na próxima etapa, a navegação ocorrerá daqui:
			 *
			 * Control:
			 * preparar sequência fixa 2/2/2.
			 *
			 * Adaptive:
			 * abrir fluxo de Steam ou preferências manuais.
			 */
		}
		catch (Exception exception)
		{
			GD.PushError(
				$"Erro inesperado ao criar sessão: {exception}");

			if (IsInstanceValid(this))
			{
				_statusLabel.Text =
					"An unexpected error occurred while " +
					"creating the session.";

				SetLoadingState(false);
			}
		}
	}

	private void SetLoadingState(bool loading)
	{
		_isCreatingSession = loading;

		_startButton.Disabled =
			loading || _selectedMode is null;

		_controlModeButton.Disabled = loading;
		_adaptiveModeButton.Disabled = loading;

		if (loading)
		{
			_statusLabel.Text =
				"Creating session...";
		}
	}

	private void OnSettingsPressed()
	{
		_statusLabel.Text =
			"Settings will be implemented later.";
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}
}
