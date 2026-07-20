#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Api;
using AdaptiveTrials.Game.Dto;
using Godot;

namespace AdaptiveTrials.Game.Bootstrap;

/// <summary>
/// Valida a comunicação inicial entre o jogo e o catálogo da API.
/// </summary>
public partial class ApiDiagnosticController : Control
{
	private Label _statusLabel = null!;
	private Button _retryButton = null!;

	private bool _isChecking;

	public override void _Ready()
	{
		_statusLabel = GetNode<Label>(
			"CenterContainer/VBoxContainer/StatusLabel");

		_retryButton = GetNode<Button>(
			"CenterContainer/VBoxContainer/RetryButton");

		_retryButton.Pressed += OnRetryButtonPressed;

		CallDeferred(MethodName.StartInitialCheck);
	}

	private void StartInitialCheck()
	{
		_ = CheckApiAsync();
	}

	private void OnRetryButtonPressed()
	{
		if (_isChecking)
		{
			return;
		}

		_ = CheckApiAsync();
	}

	private async Task CheckApiAsync()
	{
		if (_isChecking)
		{
			return;
		}

		_isChecking = true;
		SetLoadingState();

		try
		{
			ApiClient apiClient =
				GetNode<ApiClient>("/root/ApiClient");

			ApiResult<IReadOnlyList<MissionDto>> result =
				await apiClient.GetMissionsAsync();

			if (!IsInstanceValid(this))
			{
				return;
			}

			if (!result.IsSuccess || result.Data is null)
			{
				_statusLabel.Text =
					"Não foi possível conectar à API.\n\n" +
					result.ErrorMessage +
					"\n\nVerifique se o backend está em execução.";

				return;
			}

			_statusLabel.Text =
				"API conectada com sucesso.\n\n" +
				$"{result.Data.Count} missões carregadas.";

			PrintMissionSummary(result.Data);
		}
		catch (Exception exception)
		{
			GD.PushError(
				"Erro inesperado na tela de diagnóstico: " +
				exception);

			if (IsInstanceValid(this))
			{
				_statusLabel.Text =
					"Ocorreu um erro inesperado ao verificar a API.\n\n" +
					exception.Message;
			}
		}
		finally
		{
			_isChecking = false;

			if (IsInstanceValid(_retryButton))
			{
				_retryButton.Disabled = false;
			}
		}
	}

	private void SetLoadingState()
	{
		_statusLabel.Text =
			"Verificando conexão com a API...";

		_retryButton.Disabled = true;
	}

	private static void PrintMissionSummary(
		IReadOnlyList<MissionDto> missions)
	{
		GD.Print(
			$"Catálogo recebido: {missions.Count} missões.");

		foreach (MissionDto mission in missions)
		{
			GD.Print(
				$"Missão {mission.Id}: {mission.Name} | " +
				$"Tipo: {mission.Type} | " +
				$"Dificuldade: {mission.Difficulty}");
		}
	}
}
