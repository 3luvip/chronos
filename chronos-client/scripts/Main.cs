using Godot;
using System;
using System.Threading;

public partial class Main : Control
{
	private LineEdit _hostInput = null!;
	private LineEdit _portInput = null!;
	private LineEdit _serverIdInput = null!;
	private LineEdit _clientIdInput = null!;
	private LineEdit _usernameInput = null!;
	private LineEdit _passwordInput = null!;
	private CheckBox _tlsCheck = null!;
	private CheckBox _skipTlsCheck = null!;
	private CheckBox _hmacCheck = null!;
	private LineEdit _hmacSecretInput = null!;
	private Button _loginButton = null!;
	private Button _logoutButton = null!;
	private RichTextLabel _log = null!;

	private ChronosTcpClient? _client;
	private CancellationTokenSource? _cts;

	public override void _Ready()
	{
		BuildUi();
		Log("Client ready.");
	}

	public override void _ExitTree()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_client?.Dispose();
	}

	private void BuildUi()
	{
		var root = new VBoxContainer
		{
			AnchorRight = 1,
			AnchorBottom = 1,
			OffsetLeft = 16,
			OffsetTop = 16,
			OffsetRight = -16,
			OffsetBottom = -16,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		AddChild(root);

		root.AddChild(new Label { Text = "Chronos Login Client (Godot 4.6 C#)" });

		_hostInput = CreateLine(root, "Host", "127.0.0.1");
		_portInput = CreateLine(root, "Port", "14446");
		_serverIdInput = CreateLine(root, "Server ID", "1");
		_clientIdInput = CreateLine(root, "Client ID", "1001");
		_usernameInput = CreateLine(root, "Username", "admin");
		_passwordInput = CreateLine(root, "Password", "password123", true);
		_tlsCheck = new CheckBox { Text = "Use TLS", ButtonPressed = true };
		root.AddChild(_tlsCheck);
		_skipTlsCheck = new CheckBox { Text = "Skip TLS cert validation (dev)", ButtonPressed = true };
		root.AddChild(_skipTlsCheck);
		_hmacCheck = new CheckBox { Text = "Use HMAC integrity", ButtonPressed = true };
		root.AddChild(_hmacCheck);
		_hmacSecretInput = CreateLine(root, "HMAC Secret", "dev-hmac-secret-change-me", true);

		_loginButton = new Button { Text = "Connect + Login" };
		_loginButton.Pressed += OnLoginPressed;
		root.AddChild(_loginButton);
		_logoutButton = new Button { Text = "Logout", Disabled = true };
		_logoutButton.Pressed += OnLogoutPressed;
		root.AddChild(_logoutButton);

		_log = new RichTextLabel
		{
			BbcodeEnabled = false,
			FitContent = false,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		root.AddChild(_log);
	}

	private static LineEdit CreateLine(VBoxContainer root, string label, string value, bool secret = false)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(100, 0) });
		var input = new LineEdit
		{
			Text = value,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			Secret = secret
		};
		row.AddChild(input);
		root.AddChild(row);
		return input;
	}

	private async void OnLoginPressed()
	{
		_loginButton.Disabled = true;
		_cts?.Cancel();
		_cts = new CancellationTokenSource();
		_client?.Dispose();
		var options = new ClientOptions
		{
			UseTls = _tlsCheck.ButtonPressed,
			SkipTlsCertValidation = _skipTlsCheck.ButtonPressed,
			UseHmac = _hmacCheck.ButtonPressed,
			HmacSecret = _hmacSecretInput.Text
		};
		_client = new ChronosTcpClient(options);

		try
		{
			string host = _hostInput.Text.Trim();
			int port = int.Parse(_portInput.Text.Trim());
			int serverId = int.Parse(_serverIdInput.Text.Trim());
			int clientId = int.Parse(_clientIdInput.Text.Trim());
			string username = _usernameInput.Text.Trim();
			string password = _passwordInput.Text;

			Log($"Connecting to {host}:{port} ...");
			await _client.ConnectAsync(host, port, _cts.Token);
			Log("Connected.");

			Log("Sending OP_LOGIN ...");
			LoginResult result = await _client.LoginAsync(serverId, clientId, username, password, _cts.Token);
			if (!result.Ok)
			{
				Log($"Login failed: {result.Error}");
				return;
			}

			Log($"Login success. user_id={result.UserId}, gold={result.Gold}, vnd={result.Vnd}, session={result.SessionIdEcho}");
			_logoutButton.Disabled = false;
		}
		catch (Exception ex)
		{
			Log($"Error: {ex.Message}");
		}
		finally
		{
			_loginButton.Disabled = false;
		}
	}

	private async void OnLogoutPressed()
	{
		if (_client is null)
		{
			return;
		}
		try
		{
			_logoutButton.Disabled = true;
			await _client.LogoutAsync(_cts?.Token ?? CancellationToken.None);
			Log("Logout sent.");
		}
		catch (Exception ex)
		{
			Log($"Logout error: {ex.Message}");
		}
	}

	private void Log(string text)
	{
		_log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
	}
}
