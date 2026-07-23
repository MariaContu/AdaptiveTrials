using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AdaptiveTrials.Game.Dto;
using AdaptiveTrials.Game.Dto.Sessions;
using Godot;

namespace AdaptiveTrials.Game.Api;

/// <summary>
/// Centraliza as chamadas HTTP realizadas pelo jogo.
/// </summary>
public partial class ApiClient : Node
{
	private const string SettingsPath =
		"res://config/api_settings.json";

	private readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private ApiSettings _settings = new();

	public override void _Ready()
	{
		LoadSettings();
	}

	/// <summary>
	/// Obtém o catálogo completo de missões.
	/// </summary>
	public Task<ApiResult<IReadOnlyList<MissionDto>>> GetMissionsAsync()
	{
		return SendAsync<IReadOnlyList<MissionDto>>(
			HttpClient.Method.Get,
			"/api/missions");
	}

	/// <summary>
	/// Cria uma nova sessão experimental.
	/// </summary>
public async Task<ApiResult<CreateSessionResponse>> CreateSessionAsync(
	CreateSessionRequest sessionRequest)
{
	string url =
		$"{_settings.GetNormalizedBaseUrl()}/api/sessions";

	string requestBody =
		JsonSerializer.Serialize(
			sessionRequest,
			_jsonOptions);

	GD.Print(
		$"Criando sessão em: {url}. " +
		$"Payload: {requestBody}");

	HttpRequest request = new()
	{
		Timeout = _settings.TimeoutSeconds
	};

	AddChild(request);

	string[] headers =
	{
        "Content-Type: application/json"
	};

	Error requestError = request.Request(
		url,
		headers,
		HttpClient.Method.Post,
		requestBody);

	if (requestError != Error.Ok)
	{
		request.QueueFree();

		string message =
			$"A criação da sessão não pôde ser iniciada. " +
			$"Erro: {requestError}.";

		GD.PushError(message);

		return ApiResult<CreateSessionResponse>.Failure(message);
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

		const string message =
			"Ocorreu um erro ao aguardar a criação da sessão.";

		GD.PushError($"{message} Detalhes: {exception}");

		return ApiResult<CreateSessionResponse>.Failure(message);
	}

	request.QueueFree();

	HttpRequest.Result result =
		(HttpRequest.Result)(int)response[0];

	int statusCode = (int)response[1];

	byte[] bodyBytes =
		response[3].AsByteArray();

	string responseBody =
		Encoding.UTF8.GetString(bodyBytes);

	GD.Print(
		$"Resposta da criação de sessão: " +
		$"result={result}, status={statusCode}");

	if (result != HttpRequest.Result.Success)
	{
		string message = GetRequestErrorMessage(result);

		GD.PushError(message);

		return ApiResult<CreateSessionResponse>.Failure(
			message,
			statusCode > 0 ? statusCode : null);
	}

	if (statusCode < 200 || statusCode >= 300)
	{
		string message =
			$"A API rejeitou a criação da sessão " +
			$"com o status HTTP {statusCode}.";

		GD.PushError(
			$"{message} Resposta: {responseBody}");

		return ApiResult<CreateSessionResponse>.Failure(
			message,
			statusCode);
	}

	try
	{
		CreateSessionResponse? createdSession =
			JsonSerializer.Deserialize<CreateSessionResponse>(
				responseBody,
				_jsonOptions);

		if (createdSession is null)
		{
			const string message =
				"A resposta da criação da sessão está vazia.";

			return ApiResult<CreateSessionResponse>.Failure(
				message,
				statusCode);
		}

		return ApiResult<CreateSessionResponse>.Success(
			createdSession,
			statusCode);
	}
	catch (JsonException exception)
	{
		const string message =
			"A resposta da criação da sessão não corresponde " +
			"ao contrato esperado.";

		GD.PushError(
			$"{message} Detalhes: {exception.Message}. " +
			$"Resposta: {responseBody}");

		return ApiResult<CreateSessionResponse>.Failure(
			message,
			statusCode);
	}
}

/// <summary>
/// Registra o resultado comportamental de uma missão.
/// </summary>
public Task<ApiResult<bool>> RegisterSessionEventAsync(
	int sessionId,
	RegisterSessionEventRequest eventRequest)
{
	if (sessionId <= 0)
	{
		return Task.FromResult(
			ApiResult<bool>.Failure(
				"O identificador da sessão é inválido."));
	}

	ArgumentNullException.ThrowIfNull(eventRequest);

	return SendWithoutResponseAsync(
		HttpClient.Method.Post,
		$"/api/sessions/{sessionId}/events",
		eventRequest);
}

	/// <summary>
	/// Realiza uma chamada HTTP e converte a resposta para o tipo esperado.
	/// </summary>
	private async Task<ApiResult<TResponse>> SendAsync<TResponse>(
		HttpClient.Method method,
		string endpoint,
		object? requestBody = null)
	{
		string url =
			$"{_settings.GetNormalizedBaseUrl()}{endpoint}";

		HttpRequest request = new()
		{
			Timeout = _settings.TimeoutSeconds
		};

		AddChild(request);

		string[] headers =
		{
			"Content-Type: application/json",
            "Accept: application/json"
		};

		string serializedBody = requestBody is null
			? string.Empty
			: JsonSerializer.Serialize(
				requestBody,
				_jsonOptions);

		GD.Print(
			$"Enviando {method} para: {url}");

		if (!string.IsNullOrWhiteSpace(serializedBody))
		{
			GD.Print(
				$"Payload enviado: {serializedBody}");
		}

		Error requestError = request.Request(
			url,
			headers,
			method,
			serializedBody);

		if (requestError != Error.Ok)
		{
			request.QueueFree();

			string message =
				$"A requisição não pôde ser iniciada. " +
				$"Erro interno: {requestError}.";

			GD.PushError(message);

			return ApiResult<TResponse>.Failure(message);
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

			const string message =
				"Ocorreu um erro ao aguardar a resposta da API.";

			GD.PushError(
				$"{message} Detalhes: {exception}");

			return ApiResult<TResponse>.Failure(message);
		}

		request.QueueFree();

		HttpRequest.Result result =
			(HttpRequest.Result)response[0].AsInt32();

		int statusCode =
			response[1].AsInt32();

		byte[] responseBodyBytes =
			response[3].AsByteArray();

		string responseBody =
			Encoding.UTF8.GetString(responseBodyBytes);

		GD.Print(
			$"Resposta da API: " +
			$"result={result}, status={statusCode}");

		if (!string.IsNullOrWhiteSpace(responseBody))
		{
			GD.Print(
				$"Corpo recebido: {responseBody}");
		}

		if (result != HttpRequest.Result.Success)
		{
			string message =
				GetRequestErrorMessage(result);

			GD.PushError(
				$"{message} Endpoint: {url}. " +
				$"Código interno: {result}");

			return ApiResult<TResponse>.Failure(
				message,
				statusCode > 0 ? statusCode : null);
		}

		if (statusCode < 200 || statusCode >= 300)
		{
			string message =
				TryGetApiErrorMessage(responseBody) ??
				$"A API respondeu com o status HTTP {statusCode}.";

			GD.PushError(
				$"{message} Endpoint: {url}. " +
				$"Resposta: {responseBody}");

			return ApiResult<TResponse>.Failure(
				message,
				statusCode);
		}

		if (string.IsNullOrWhiteSpace(responseBody))
		{
			const string message =
				"A API retornou uma resposta vazia.";

			GD.PushError(message);

			return ApiResult<TResponse>.Failure(
				message,
				statusCode);
		}

		try
		{
			TResponse? data =
				JsonSerializer.Deserialize<TResponse>(
					responseBody,
					_jsonOptions);

			if (data is null)
			{
				const string message =
					"A resposta da API não pôde ser interpretada.";

				GD.PushError(message);

				return ApiResult<TResponse>.Failure(
					message,
					statusCode);
			}

			return ApiResult<TResponse>.Success(
				data,
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

			return ApiResult<TResponse>.Failure(
				message,
				statusCode);
		}
		catch (Exception exception)
		{
			const string message =
				"Ocorreu um erro inesperado ao processar " +
				"a resposta da API.";

			GD.PushError(
				$"{message} Detalhes: {exception}");

			return ApiResult<TResponse>.Failure(
				message,
				statusCode);
		}
	}
	
	/// <summary>
/// Realiza uma chamada para endpoints que podem responder
/// sem um corpo JSON.
/// </summary>
private async Task<ApiResult<bool>> SendWithoutResponseAsync(
	HttpClient.Method method,
	string endpoint,
	object? requestBody = null)
{
	string url =
		$"{_settings.GetNormalizedBaseUrl()}{endpoint}";

	HttpRequest request = new()
	{
		Timeout = _settings.TimeoutSeconds
	};

	AddChild(request);

	string[] headers =
	{
		"Content-Type: application/json",
		"Accept: application/json"
	};

	string serializedBody =
		requestBody is null
			? string.Empty
			: JsonSerializer.Serialize(
				requestBody,
				_jsonOptions);

	GD.Print(
		$"Enviando {method} para: {url}");

	if (!string.IsNullOrWhiteSpace(serializedBody))
	{
		GD.Print(
			$"Payload enviado: {serializedBody}");
	}

	Error requestError =
		request.Request(
			url,
			headers,
			method,
			serializedBody);

	if (requestError != Error.Ok)
	{
		request.QueueFree();

		string message =
			$"A requisição não pôde ser iniciada. " +
			$"Erro interno: {requestError}.";

		GD.PushError(message);

		return ApiResult<bool>.Failure(message);
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

		const string message =
			"Ocorreu um erro ao aguardar a resposta da API.";

		GD.PushError(
			$"{message} Detalhes: {exception}");

		return ApiResult<bool>.Failure(message);
	}

	request.QueueFree();

	HttpRequest.Result result =
		(HttpRequest.Result)response[0].AsInt32();

	int statusCode =
		response[1].AsInt32();

	byte[] responseBodyBytes =
		response[3].AsByteArray();

	string responseBody =
		Encoding.UTF8.GetString(
			responseBodyBytes);

	GD.Print(
		$"Resposta da API: " +
		$"result={result}, status={statusCode}");

	if (!string.IsNullOrWhiteSpace(responseBody))
	{
		GD.Print(
			$"Corpo recebido: {responseBody}");
	}

	if (result != HttpRequest.Result.Success)
	{
		string message =
			GetRequestErrorMessage(result);

		GD.PushError(
			$"{message} Endpoint: {url}. " +
			$"Código interno: {result}");

		return ApiResult<bool>.Failure(
			message,
			statusCode > 0
				? statusCode
				: null);
	}

	if (statusCode < 200 ||
		statusCode >= 300)
	{
		string message =
			TryGetApiErrorMessage(responseBody) ??
			$"A API respondeu com o status HTTP " +
			$"{statusCode}.";

		GD.PushError(
			$"{message} Endpoint: {url}. " +
			$"Resposta: {responseBody}");

		return ApiResult<bool>.Failure(
			message,
			statusCode);
	}

	return ApiResult<bool>.Success(
		true,
		statusCode);
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
			FileAccess.Open(
				SettingsPath,
				FileAccess.ModeFlags.Read);

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

			if (string.IsNullOrWhiteSpace(
					loadedSettings.BaseUrl))
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
			$"Timeout da API: " +
			$"{_settings.TimeoutSeconds} segundos.");
	}

	private string? TryGetApiErrorMessage(
		string responseBody)
	{
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			return null;
		}

		try
		{
			using JsonDocument document =
				JsonDocument.Parse(responseBody);

			if (!document.RootElement.TryGetProperty(
					"message",
					out JsonElement messageElement))
			{
				return null;
			}

			return messageElement.GetString();
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string GetRequestErrorMessage(
		HttpRequest.Result result)
	{
		return result switch
		{
			HttpRequest.Result.CantConnect =>
				"Não foi possível estabelecer conexão com a API.",

			HttpRequest.Result.CantResolve =>
				"O endereço configurado para a API " +
				"não pôde ser resolvido.",

			HttpRequest.Result.ConnectionError =>
				"A conexão com a API foi interrompida.",

			HttpRequest.Result.TlsHandshakeError =>
				"Ocorreu um erro no certificado HTTPS da API.",

			HttpRequest.Result.Timeout =>
				"A comunicação com a API excedeu " +
				"o tempo limite.",

			HttpRequest.Result.BodySizeLimitExceeded =>
				"A resposta da API excedeu " +
				"o tamanho permitido.",

			_ =>
				$"A requisição para a API falhou: {result}."
		};
	}
}
