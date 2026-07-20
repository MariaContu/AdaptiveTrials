#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Dto;
using Godot;

namespace AdaptiveTrials.Game.Api;

/// <summary>
/// Centraliza as chamadas HTTP realizadas pelo jogo.
/// </summary>
public partial class ApiClient : Node
{
	private const string SettingsPath = "res://config/api_settings.json";

	private readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private ApiSettings _settings = new();

	public override void _Ready()
	{
		LoadSettings();
	}

	public async Task<ApiResult<IReadOnlyList<MissionDto>>> GetMissionsAsync()
	{
		string url =
			$"{_settings.GetNormalizedBaseUrl()}/api/missions";

		GD.Print($"Consultando catálogo em: {url}");

		HttpRequest request = new()
		{
			Timeout = _settings.TimeoutSeconds
		};

		AddChild(request);

		Error requestError = request.Request(url);

		if (requestError != Error.Ok)
		{
			request.QueueFree();

			string message =
				$"A requisição não pôde ser iniciada. Erro: {requestError}.";

			GD.PushError(message);

			return ApiResult<IReadOnlyList<MissionDto>>.Failure(message);
		}

		Variant[] response;

		try
		{
			response = await ToSignal(
				request,
				HttpRequest.SignalName.RequestCompleted);
		}
		catch (Exception exception)
		{
			request.QueueFree();

			string message =
				"Ocorreu um erro ao aguardar a resposta da API.";

			GD.PushError($"{message} Detalhes: {exception}");

			return ApiResult<IReadOnlyList<MissionDto>>.Failure(message);
		}

		request.QueueFree();

		HttpRequest.Result result =
			(HttpRequest.Result)(int)response[0];

		int statusCode = (int)response[1];

		byte[] responseBodyBytes =
			(byte[])response[3];

		string responseBody =
			System.Text.Encoding.UTF8.GetString(responseBodyBytes);

		GD.Print(
			$"Resposta da API: result={result}, " +
			$"status={statusCode}");

		if (result != HttpRequest.Result.Success)
		{
			string message = GetRequestErrorMessage(result);

			GD.PushError(
				$"{message} Endpoint: {url}. " +
				$"Código interno: {result}");

			return ApiResult<IReadOnlyList<MissionDto>>.Failure(
				message,
				statusCode > 0 ? statusCode : null);
		}

		if (statusCode < 200 || statusCode >= 300)
		{
			string message =
				$"A API respondeu com o status HTTP {statusCode}.";

			GD.PushError(
				$"{message} Endpoint: {url}. " +
				$"Resposta: {responseBody}");

			return ApiResult<IReadOnlyList<MissionDto>>.Failure(
				message,
				statusCode);
		}

		try
		{
			List<MissionDto>? missions =
				JsonSerializer.Deserialize<List<MissionDto>>(
					responseBody,
					_jsonOptions);

			if (missions is null)
			{
				const string message =
					"A resposta da API não contém um catálogo válido.";

				GD.PushError(message);

				return ApiResult<IReadOnlyList<MissionDto>>.Failure(
					message,
					statusCode);
			}

			GD.Print(
				$"Catálogo desserializado: {missions.Count} missões.");

			return ApiResult<IReadOnlyList<MissionDto>>.Success(
				missions,
				statusCode);
		}
		catch (JsonException exception)
		{
			const string message =
				"A API retornou um JSON incompatível " +
				"com o contrato esperado.";

			GD.PushError(
				$"{message}\n" +
				$"Detalhes: {exception.Message}\n" +
				$"Resposta recebida: {responseBody}");

			return ApiResult<IReadOnlyList<MissionDto>>.Failure(
				message,
				statusCode);
		}
		catch (Exception exception)
		{
			const string message =
				"Ocorreu um erro inesperado ao processar " +
				"a resposta da API.";

			GD.PushError($"{message} Detalhes: {exception}");

			return ApiResult<IReadOnlyList<MissionDto>>.Failure(
				message,
				statusCode);
		}
	}

	private void LoadSettings()
	{
		if (!FileAccess.FileExists(SettingsPath))
		{
			GD.PushWarning(
				$"O arquivo {SettingsPath} não foi encontrado. " +
				"As configurações padrão serão utilizadas.");

			PrintCurrentSettings();
			return;
		}

		using FileAccess file =
			FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);

		string content = file.GetAsText();

		try
		{
			ApiSettings? loadedSettings =
				JsonSerializer.Deserialize<ApiSettings>(
					content,
					_jsonOptions);

			if (loadedSettings is null)
			{
				GD.PushWarning(
					"O arquivo de configuração da API " +
					"está vazio ou inválido.");

				PrintCurrentSettings();
				return;
			}

			if (string.IsNullOrWhiteSpace(loadedSettings.BaseUrl))
			{
				GD.PushWarning(
					"A URL-base da API está vazia. " +
					"A configuração padrão será utilizada.");

				PrintCurrentSettings();
				return;
			}

			if (loadedSettings.TimeoutSeconds <= 0)
			{
				loadedSettings.TimeoutSeconds = 10;
			}

			_settings = loadedSettings;

			PrintCurrentSettings();
		}
		catch (JsonException exception)
		{
			GD.PushError(
				"Não foi possível ler as configurações da API. " +
				$"Detalhes: {exception.Message}");

			PrintCurrentSettings();
		}
	}

	private void PrintCurrentSettings()
	{
		GD.Print(
			$"ApiClient configurado para: " +
			$"{_settings.GetNormalizedBaseUrl()}");

		GD.Print(
			$"Timeout da API: {_settings.TimeoutSeconds} segundos.");
	}

	private static string GetRequestErrorMessage(
		HttpRequest.Result result)
	{
		return result switch
		{
			HttpRequest.Result.CantConnect =>
				"Não foi possível estabelecer conexão com a API.",

			HttpRequest.Result.CantResolve =>
				"O endereço configurado para a API não pôde ser resolvido.",

			HttpRequest.Result.ConnectionError =>
				"A conexão com a API foi interrompida.",

			HttpRequest.Result.TlsHandshakeError =>
				"Ocorreu um erro no certificado HTTPS da API.",

			HttpRequest.Result.Timeout =>
				"A comunicação com a API excedeu o tempo limite.",

			HttpRequest.Result.BodySizeLimitExceeded =>
				"A resposta da API excedeu o tamanho permitido.",

			_ =>
				$"A requisição para a API falhou: {result}."
		};
	}
}
