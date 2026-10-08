#nullable enable

using System;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Api;
using AdaptiveTrials.Game.Dto.Adaptive;
using AdaptiveTrials.Game.Enums;
using AdaptiveTrials.Game.Session;
using Godot;

namespace AdaptiveTrials.Game.Adaptive;

/// <summary>
/// Configura o perfil inicial utilizado pelo modo Adaptativo.
/// O fluxo suporta Steam + IA, preferências manuais e perfil neutro 33/33/33.
/// </summary>
public partial class AdaptiveProfileSetupController : Control
{
    private const string MainMenuScenePath = "res://scenes/menu/MainMenu.tscn";

    [Export]
    public bool TestMode { get; set; }

    private ApiClient? _apiClient;
    private SessionManager? _sessionManager;

    private Button _backButton = null!;
    private Label _stepLabel = null!;
    private Label _statusLabel = null!;

    private PanelContainer _steamEntryPanel = null!;
    private LineEdit _steamIdInput = null!;
    private Button _searchSteamButton = null!;
    private Button _noSteamButton = null!;

    private PanelContainer _steamConfirmPanel = null!;
    private TextureRect _avatarTexture = null!;
    private Label _avatarPlaceholder = null!;
    private Label _steamNameLabel = null!;
    private Label _steamIdLabel = null!;
    private Label _steamRegionLabel = null!;
    private Label _steamVisibilityLabel = null!;
    private Button _confirmSteamButton = null!;
    private Button _wrongSteamButton = null!;

    private PanelContainer _manualPanel = null!;
    private HSlider _combatSlider = null!;
    private HSlider _explorationSlider = null!;
    private HSlider _puzzleSlider = null!;
    private Label _combatValueLabel = null!;
    private Label _explorationValueLabel = null!;
    private Label _puzzleValueLabel = null!;
    private Button _manualCancelButton = null!;
    private Button _saveManualButton = null!;

    private PanelContainer _readyPanel = null!;
    private Label _readySourceLabel = null!;
    private Label _readyPreferenceLabel = null!;
    private Label _readyPreferenceDetail = null!;
    private Label _readyCombatLabel = null!;
    private Label _readyExplorationLabel = null!;
    private Label _readyPuzzleLabel = null!;
    private Button _continueButton = null!;

    private SteamProfilePreviewResponse? _steamPreview;
    private bool _isBusy;

    public override void _Ready()
    {
        GetNodes();
        ConnectSignals();

        if (!TestMode)
        {
            _apiClient = GetNode<ApiClient>("/root/ApiClient");
            _sessionManager = GetNode<SessionManager>("/root/SessionManager");

            if (!_sessionManager.HasActiveSession
                || _sessionManager.Mode != GameMode.Adaptive
                || !_sessionManager.PlayerId.HasValue)
            {
                _statusLabel.Text =
                    "Não existe uma sessão adaptativa ativa. Volte ao menu e inicie novamente.";
                SetInteractionEnabled(false);
                return;
            }
        }

        ResetManualPreferences();
        ShowSteamEntry();

        if (TestMode)
        {
            _statusLabel.Text =
                "MODO DE TESTE — digite qualquer identificador e pressione Enter ou BUSCAR.";
        }
    }

    private void GetNodes()
    {
        _backButton = GetNode<Button>("InterfaceMargin/Root/TopBar/BackButton");
        _stepLabel = GetNode<Label>("InterfaceMargin/Root/TitleArea/StepLabel");
        _statusLabel = GetNode<Label>("InterfaceMargin/Root/StatusLabel");

        _steamEntryPanel = GetNode<PanelContainer>("InterfaceMargin/Root/ContentCenter/SteamEntryPanel");
        _steamIdInput = GetNode<LineEdit>("InterfaceMargin/Root/ContentCenter/SteamEntryPanel/Margin/Content/SteamIdInput");
        _searchSteamButton = GetNode<Button>("InterfaceMargin/Root/ContentCenter/SteamEntryPanel/Margin/Content/Actions/SearchSteamButton");
        _noSteamButton = GetNode<Button>("InterfaceMargin/Root/ContentCenter/SteamEntryPanel/Margin/Content/Actions/NoSteamButton");

        _steamConfirmPanel = GetNode<PanelContainer>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel");
        _avatarTexture = GetNode<TextureRect>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/ProfileRow/AvatarFrame/AvatarTexture");
        _avatarPlaceholder = GetNode<Label>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/ProfileRow/AvatarFrame/AvatarPlaceholder");
        _steamNameLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/ProfileRow/Details/SteamNameLabel");
        _steamIdLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/ProfileRow/Details/SteamIdLabel");
        _steamRegionLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/ProfileRow/Details/SteamRegionLabel");
        _steamVisibilityLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/ProfileRow/Details/SteamVisibilityLabel");
        _confirmSteamButton = GetNode<Button>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/Actions/ConfirmSteamButton");
        _wrongSteamButton = GetNode<Button>("InterfaceMargin/Root/ContentCenter/SteamConfirmPanel/Margin/Content/Actions/WrongSteamButton");

        _manualPanel = GetNode<PanelContainer>("InterfaceMargin/Root/ContentCenter/ManualPanel");
        _combatSlider = GetNode<HSlider>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/CombatRow/CombatSlider");
        _explorationSlider = GetNode<HSlider>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/ExplorationRow/ExplorationSlider");
        _puzzleSlider = GetNode<HSlider>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/PuzzleRow/PuzzleSlider");
        _combatValueLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/CombatRow/CombatValueLabel");
        _explorationValueLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/ExplorationRow/ExplorationValueLabel");
        _puzzleValueLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/PuzzleRow/PuzzleValueLabel");
        _manualCancelButton = GetNode<Button>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/Actions/ManualCancelButton");
        _saveManualButton = GetNode<Button>("InterfaceMargin/Root/ContentCenter/ManualPanel/Margin/Content/Actions/SaveManualButton");

        _readyPanel = GetNode<PanelContainer>("InterfaceMargin/Root/ContentCenter/ReadyPanel");
        _readySourceLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ReadyPanel/Margin/Content/ReadySourceLabel");
        _readyPreferenceLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ReadyPanel/Margin/Content/ReadyPreferenceLabel");
        _readyPreferenceDetail = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ReadyPanel/Margin/Content/ReadyPreferenceDetail");
        _readyCombatLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ReadyPanel/Margin/Content/Distribution/ReadyCombatLabel");
        _readyExplorationLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ReadyPanel/Margin/Content/Distribution/ReadyExplorationLabel");
        _readyPuzzleLabel = GetNode<Label>("InterfaceMargin/Root/ContentCenter/ReadyPanel/Margin/Content/Distribution/ReadyPuzzleLabel");
        _continueButton = GetNode<Button>("InterfaceMargin/Root/ContentCenter/ReadyPanel/Margin/Content/ContinueButton");
    }

    private void ConnectSignals()
    {
        _backButton.Pressed += OnBackPressed;
        _searchSteamButton.Pressed += OnSearchSteamPressed;
        _noSteamButton.Pressed += () => ShowManualPreferences();
        _confirmSteamButton.Pressed += OnConfirmSteamPressed;
        _wrongSteamButton.Pressed += ShowSteamEntry;
        _manualCancelButton.Pressed += OnManualCancelPressed;
        _saveManualButton.Pressed += OnSaveManualPressed;
        _continueButton.Pressed += OnContinuePressed;
        _steamIdInput.TextSubmitted += _ => OnSearchSteamPressed();

        _combatSlider.ValueChanged += _ => UpdateManualPercentages();
        _explorationSlider.ValueChanged += _ => UpdateManualPercentages();
        _puzzleSlider.ValueChanged += _ => UpdateManualPercentages();
    }

    private void OnSearchSteamPressed()
    {
        if (_isBusy)
        {
            return;
        }

        _ = SearchSteamAsync();
    }

    private async Task SearchSteamAsync()
    {
        string identifier = _steamIdInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(identifier))
        {
            _statusLabel.Text = "Informe um SteamID, nome personalizado ou URL de perfil.";
            return;
        }

        SetBusy(true, "Buscando perfil público na Steam...");

        if (TestMode)
        {
            _steamPreview = new SteamProfilePreviewResponse
            {
                SteamId = "76561198000000000",
                PersonaName = "Jogador de Teste",
                CountryCode = "BR",
                IsCommunityProfilePublic = true
            };

            ShowSteamConfirmation(_steamPreview);
            SetBusy(false, "Perfil de teste encontrado.");
            return;
        }

        ApiResult<SteamProfilePreviewResponse> result =
            await _apiClient!.GetSteamProfilePreviewAsync(identifier);

        if (!IsInstanceValid(this))
        {
            return;
        }

        if (!result.IsSuccess || result.Data is null)
        {
            SetBusy(false,
                "Não foi possível localizar esse perfil. Você pode tentar novamente ou usar preferências manuais.");
            return;
        }

        _steamPreview = result.Data;
        ShowSteamConfirmation(_steamPreview);
        SetBusy(false, "Confira se este é o seu perfil antes de continuar.");
        _ = LoadAvatarAsync(_steamPreview.AvatarUrl, _steamPreview.SteamId);
    }

    private void ShowSteamConfirmation(SteamProfilePreviewResponse preview)
    {
        ShowOnly(_steamConfirmPanel);
        _stepLabel.Text = "2 de 3 · Confirme seu perfil";
        _steamNameLabel.Text = preview.PersonaName;
        _steamIdLabel.Text = $"SteamID: {preview.SteamId}";
        _steamRegionLabel.Text = $"Região: {FormatRegion(preview.CountryCode)}";
        _steamVisibilityLabel.Text = preview.IsCommunityProfilePublic
            ? "Perfil da comunidade: público"
            : "Perfil da comunidade: restrito";
        _avatarTexture.Texture = null;
        _avatarPlaceholder.Visible = true;
    }

    private void OnConfirmSteamPressed()
    {
        if (_isBusy || _steamPreview is null)
        {
            return;
        }

        _ = ImportSteamAsync();
    }

    private async Task ImportSteamAsync()
    {
        SetBusy(true, "Analisando sua biblioteca e preparando o perfil adaptativo...");

        if (TestMode)
        {
            ShowReadyProfile("Steam + IA (teste)", 0.52, 0.28, 0.20);
            SetBusy(false, "Perfil de teste preparado com sucesso.");
            return;
        }

        int playerId = _sessionManager!.PlayerId!.Value;
        ApiResult<SteamImportResponse> result =
            await _apiClient!.ImportSteamProfileAsync(playerId, _steamPreview!.SteamId);

        if (!IsInstanceValid(this))
        {
            return;
        }

        if (!result.IsSuccess || result.Data is null)
        {
            SetBusy(false);
            ShowManualPreferences(
                "Não foi possível gerar um perfil utilizável a partir dessa biblioteca Steam. " +
                "Use as preferências manuais para continuar.");
            return;
        }

        SteamImportResponse profile = result.Data;
        ShowReadyProfile("Steam + IA", profile.Combat, profile.Exploration, profile.Puzzle, profile.PredictedCategory);
        SetBusy(false, "Perfil adaptativo preparado com sucesso.");
    }

    private void ShowManualPreferences(string? message = null)
    {
        ShowOnly(_manualPanel);
        _stepLabel.Text = "2 de 3 · Suas preferências";
        UpdateManualPercentages();
        _statusLabel.Text = message ??
            "Ajuste as três categorias. Se não tiver preferência, mantenha o perfil equilibrado.";
    }

    private void OnManualCancelPressed()
    {
        ResetManualPreferences();
        ShowSteamEntry();
    }

    private void OnSaveManualPressed()
    {
        if (_isBusy)
        {
            return;
        }

        _ = SaveManualPreferencesAsync();
    }

    private async Task SaveManualPreferencesAsync()
    {
        double combat = _combatSlider.Value;
        double exploration = _explorationSlider.Value;
        double puzzle = _puzzleSlider.Value;

        if (combat + exploration + puzzle <= 0)
        {
            combat = 1;
            exploration = 1;
            puzzle = 1;
        }

        SetBusy(true, "Salvando preferências...");

        if (TestMode)
        {
            Normalize(ref combat, ref exploration, ref puzzle);
            ShowReadyProfile("Preferências manuais (teste)", combat, exploration, puzzle);
            SetBusy(false, "Preferências de teste salvas.");
            return;
        }

        int playerId = _sessionManager!.PlayerId!.Value;
        ApiResult<PlayerProfileResponse> result =
            await _apiClient!.RegisterManualPreferencesAsync(
                playerId,
                combat,
                exploration,
                puzzle);

        if (!IsInstanceValid(this))
        {
            return;
        }

        if (!result.IsSuccess || result.Data is null)
        {
            SetBusy(false,
                "Não foi possível salvar as preferências. Verifique a API e tente novamente.");
            return;
        }

        PlayerProfileResponse profile = result.Data;
        ShowReadyProfile("Preferências manuais", profile.Combat, profile.Exploration, profile.Puzzle);
        SetBusy(false, "Perfil adaptativo preparado com sucesso.");
    }

    private void ShowReadyProfile(
        string source,
        double combat,
        double exploration,
        double puzzle,
        string? predictedCategory = null)
    {
        Normalize(ref combat, ref exploration, ref puzzle);
        ShowOnly(_readyPanel);
        _stepLabel.Text = "3 de 3 · Perfil pronto";
        _readySourceLabel.Text = $"Origem: {source}";

        // Para Steam, usamos a classe efetivamente prevista pelo modelo.
        // Para preferências manuais, usamos a maior preferência normalizada.
        string category = source.StartsWith("Steam", StringComparison.OrdinalIgnoreCase)
            ? FormatPredictedCategory(predictedCategory, combat, exploration, puzzle)
            : DominantCategory(combat, exploration, puzzle);
        _readyPreferenceLabel.Text = $"Seu perfil parece gostar mais de: {category}";
        _readyPreferenceDetail.Text = source.StartsWith("Steam", StringComparison.OrdinalIgnoreCase)
            ? "Classificação estimada a partir da biblioteca Steam pelo modelo de IA."
            : "Preferência indicada por você na configuração inicial.";
        _readyCombatLabel.Text = $"Combate\n{combat * 100:F1}%";
        _readyExplorationLabel.Text = $"Exploração\n{exploration * 100:F1}%";
        _readyPuzzleLabel.Text = $"Quebra-cabeça\n{puzzle * 100:F1}%";
    }

    private void OnContinuePressed()
    {
        _statusLabel.Text = TestMode
            ? "FLUXO DE PERFIL APROVADO — Steam, fallback manual e perfil final podem ser testados isoladamente."
            : "Perfil pronto. A primeira recomendação adaptativa será conectada na próxima etapa.";
    }

    private void OnBackPressed()
    {
        if (_isBusy)
        {
            return;
        }

        if (!_steamEntryPanel.Visible)
        {
            ShowSteamEntry();
            return;
        }

        if (TestMode)
        {
            _statusLabel.Text = "MODO DE TESTE — fluxo reiniciado.";
            return;
        }

        _ = ReturnToMenuAsync();
    }

    private async Task ReturnToMenuAsync()
    {
        SetBusy(true, "Encerrando configuração...");

        if (_sessionManager is not null && _sessionManager.HasActiveSession)
        {
            bool ended = await _sessionManager.EndCurrentSessionAsync();

            if (!ended)
            {
                GD.PushWarning(
                    "Não foi possível encerrar a sessão adaptativa ao voltar para o menu. " +
                    "O estado local será limpo mesmo assim.");
            }

            _sessionManager.ClearSession();
        }

        if (!IsInstanceValid(this))
        {
            return;
        }

        Error error = GetTree().ChangeSceneToFile(MainMenuScenePath);

        if (error != Error.Ok)
        {
            SetBusy(false, $"Não foi possível voltar ao menu. Erro: {error}");
        }
    }

    private void ShowSteamEntry()
    {
        ShowOnly(_steamEntryPanel);
        _stepLabel.Text = "1 de 3 · Identifique seu perfil";
        _steamPreview = null;
        _avatarTexture.Texture = null;
        _avatarPlaceholder.Visible = true;
        _statusLabel.Text = TestMode
            ? "MODO DE TESTE — digite qualquer identificador e pressione Enter ou BUSCAR."
            : "Use sua Steam para gerar o perfil inicial ou continue sem Steam.";

        SetInteractionEnabled(true);
        Callable.From(FocusSteamInput).CallDeferred();
    }

    private void FocusSteamInput()
    {
        if (!_steamEntryPanel.Visible || !_steamIdInput.Editable)
        {
            return;
        }

        _steamIdInput.GrabFocus();
        _steamIdInput.CaretColumn = _steamIdInput.Text.Length;
    }

    private void ResetManualPreferences()
    {
        _combatSlider.Value = 33;
        _explorationSlider.Value = 33;
        _puzzleSlider.Value = 33;
        UpdateManualPercentages();
    }

    private void UpdateManualPercentages()
    {
        double combat = _combatSlider.Value;
        double exploration = _explorationSlider.Value;
        double puzzle = _puzzleSlider.Value;

        if (combat + exploration + puzzle <= 0)
        {
            combat = exploration = puzzle = 1;
        }

        Normalize(ref combat, ref exploration, ref puzzle);
        _combatValueLabel.Text = $"{combat * 100:F0}%";
        _explorationValueLabel.Text = $"{exploration * 100:F0}%";
        _puzzleValueLabel.Text = $"{puzzle * 100:F0}%";
    }

    private void ShowOnly(Control visiblePanel)
    {
        _steamEntryPanel.Visible = visiblePanel == _steamEntryPanel;
        _steamConfirmPanel.Visible = visiblePanel == _steamConfirmPanel;
        _manualPanel.Visible = visiblePanel == _manualPanel;
        _readyPanel.Visible = visiblePanel == _readyPanel;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        SetInteractionEnabled(!busy);

        if (!string.IsNullOrWhiteSpace(message))
        {
            _statusLabel.Text = message;
        }
    }

    private void SetInteractionEnabled(bool enabled)
    {
        _backButton.Disabled = !enabled;
        _searchSteamButton.Disabled = !enabled;
        _noSteamButton.Disabled = !enabled;
        _confirmSteamButton.Disabled = !enabled;
        _wrongSteamButton.Disabled = !enabled;
        _manualCancelButton.Disabled = !enabled;
        _saveManualButton.Disabled = !enabled;
        _continueButton.Disabled = !enabled;
        _steamIdInput.Editable = enabled;
        _combatSlider.Editable = enabled;
        _explorationSlider.Editable = enabled;
        _puzzleSlider.Editable = enabled;
    }

    private async Task LoadAvatarAsync(string avatarUrl, string requestedSteamId)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return;
        }

        HttpRequest request = new() { Timeout = 10 };
        AddChild(request);

        Error startError = request.Request(avatarUrl);

        if (startError != Error.Ok)
        {
            request.QueueFree();
            return;
        }

        Variant[] response;

        try
        {
            response = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
        }
        catch
        {
            request.QueueFree();
            return;
        }

        request.QueueFree();

        if (!IsInstanceValid(this)
            || (HttpRequest.Result)response[0].AsInt32() != HttpRequest.Result.Success
            || response[1].AsInt32() < 200
            || response[1].AsInt32() >= 300)
        {
            return;
        }

        byte[] bytes = response[3].AsByteArray();
        Image image = new();
        Error imageError = image.LoadJpgFromBuffer(bytes);

        if (imageError != Error.Ok)
        {
            imageError = image.LoadPngFromBuffer(bytes);
        }

        if (imageError != Error.Ok)
        {
            return;
        }

        if (!_steamConfirmPanel.Visible
            || _steamPreview?.SteamId != requestedSteamId)
        {
            return;
        }

        _avatarTexture.Texture = ImageTexture.CreateFromImage(image);
        _avatarPlaceholder.Visible = false;
    }

    private static string DominantCategory(double combat, double exploration, double puzzle)
    {
        double max = Math.Max(combat, Math.Max(exploration, puzzle));
        double min = Math.Min(combat, Math.Min(exploration, puzzle));
        if (max - min < 0.02)
        {
            return "Perfil equilibrado";
        }

        if (combat >= exploration && combat >= puzzle) return "Combate";
        if (exploration >= combat && exploration >= puzzle) return "Exploração";
        return "Quebra-cabeça";
    }

    private static string FormatPredictedCategory(
        string? predictedCategory, double combat, double exploration, double puzzle)
    {
        return predictedCategory?.Trim().ToLowerInvariant() switch
        {
            "combat" => "Combate",
            "exploration" => "Exploração",
            "strategic_reasoning" => "Quebra-cabeça",
            _ => DominantCategory(combat, exploration, puzzle)
        };
    }

    private static string FormatRegion(string countryCode)
    {
        return string.IsNullOrWhiteSpace(countryCode)
            ? "não informada"
            : countryCode.ToUpperInvariant();
    }

    private static void Normalize(
        ref double combat,
        ref double exploration,
        ref double puzzle)
    {
        double total = combat + exploration + puzzle;

        if (total <= 0)
        {
            combat = exploration = puzzle = 1.0 / 3.0;
            return;
        }

        combat /= total;
        exploration /= total;
        puzzle /= total;
    }
}
