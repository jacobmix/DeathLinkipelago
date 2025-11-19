using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CreepyUtil.Archipelago;
using Godot;
using static Archipelago.MultiClient.Net.Enums.ItemsHandlingFlags;
using static DeathLinkipelago.Scripts.DeathTracker;
using static DeathLinkipelago.Scripts.MainController;

using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DeathLinkipelago.Scripts;

public static class DiscordWebhook
{
	private static readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient();

	public static async Task SendMessageAsync(string webhookUrl, string message)
	{
		if (string.IsNullOrWhiteSpace(webhookUrl)) return;

		var payload = new
		{
			content = message
		};

		var json = JsonSerializer.Serialize(payload);
		var httpContent = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");

		try
		{
			await _http.PostAsync(webhookUrl, httpContent);
		}
		catch
		{
			// optionally log the error somewhere
		}
	}
}

public partial class Login : Control
{
	[Export] private MainController _Main;
	[Export] private LineEdit _AddressEdit;
	[Export] private LineEdit _PasswordEdit;
	[Export] private LineEdit _PortEdit;
	[Export] private LineEdit _SlotEdit;
	[Export] private Button _Login;
	[Export] private Label _ErrorLabel;
	private string _LastText;

	private bool _manualDisconnect = false;    // set when user manually disconnects
	private bool _reconnectRunning = false;    // ensure only one reconnect loop runs

	public string Address
	{
		get => _AddressEdit.Text;
		set => _AddressEdit.Text = value;
	}

	public string Password
	{
		get => _PasswordEdit.Text;
		set => _PasswordEdit.Text = value;
	}

	public string Slot
	{
		get => _SlotEdit.Text;
		set => _SlotEdit.Text = value;
	}

	public int Port { get; private set; } = 12345;

	public int PortField
	{
		get => Port;
		set => _PortEdit.Text = $"{Port = value}";
	}

	public override void _Ready()
	{
		_PortEdit.TextChanged += s =>
		{
			if (s.Trim() == "" || s.IsValidInt())
			{
				Port = int.TryParse(s.Trim(), out var port) ? port : 12345;
				_LastText = $"{Port}";
				return;
			}

			_PortEdit.Text = _LastText;
		};

		_Login.Pressed += TryConnection;
		// -------------------------
		// Auto-connect using command-line arguments
		// Usage (positional): <address> <port> <password> <slot>
		// Example:
		// DeathLinkipelago.exe my.ap.server 38281 mypass PlayerSlot
		// -------------------------
		try
		{
			var args = OS.GetCmdlineArgs();
			if (args != null && args.Length >= 4)
			{
				// positional args: address, port, password, slot
				Address = args[0];
				if (int.TryParse(args[1], out var p)) PortField = p;
				Password = args[2];
				Slot = args[3];
				// optional: additional args after slot are ignored
				// auto attempt connect
				// small delay is not necessary; TryConnection already handles background connecting
				TryConnection();
			}
		}
		catch
		{
			// ignore any unexpected errors reading args; keep manual UI behavior
		}
	}

	public void TryConnection()
	{
		// user initiated => clear manual disconnect flag
		_manualDisconnect = false;

		Lock(true);
		// Listen for unexpected connection loss
		Client = new ApClient();
		Client.OnConnectionLost += (_, _) => CallDeferred("OnUnexpectedDisconnect");
		var info = GetLoginInfo();

		Task.Run(() =>
		{
			try
			{
				string[] error;
				lock (Client)
				{
					error = Client.TryConnect(info, 0x0AF5F0AC,
						"DeathLinkipelago", AllItems,
						tags: ["DeathLink"]);
				}

				if (error is not null && error.Length > 0)
				{
					CallDeferred("ConnectionFailed", error);
				}
				else
				{
					CallDeferred("Connected");
				}
			}
			catch (Exception e)
			{
				CallDeferred("ConnectionFailed", [e.Message, e.StackTrace]);
			}

			CallDeferred("Lock", false);
		});
	}

	public void TryDisconnection()
	{
		// mark manual disconnect so reconnection doesn't start
		_manualDisconnect = true;
		// if there's a client, request disconnect
		Client?.TryDisconnect();
		Disconnected();
	}

	public void Lock(bool toggle)
	{
		_Login.Disabled = toggle;
		_AddressEdit.Editable = !toggle;
		_PasswordEdit.Editable = !toggle;
		_PortEdit.Editable = !toggle;
		_SlotEdit.Editable = !toggle;
	}

	public void Connected()
	{
		var version = Client.SlotData.GetValueOrDefault("compatibility_version", "None");
		if (version is null or "None" or not ApWorldCompatibilityVersion)
		{
			ConnectionFailed([
				$"Incorrect version compatability.\nClient: [{ApWorldCompatibilityVersion}], ApWorld: [{version}]"
			]);
			return;
		}

		Client.OnConnectionEvent += (_, _) => TryConnection();
		_Main.Connected();
	}

	public void Disconnected()
	{
		if (Client is not null)
		{
			Client = null;
		}

		MainController.Config = null;
		DeathCounter = [];
		LastSave = 0;
		LastDeathTrap = 0;
		NextLifeCoin = 0;
		ShopLevel = 1;
		HasChangedSinceLastSave = false;
		Reset();
		HintedItems = [];
		PrioritizedItems = [];

		Inventory = new()
		{
			["Death Trap"] = 0,
			["Death Shield"] = 0,
			["Death Coin"] = 0,
			["Life Coin"] = 0
		};

		InventoryUsed = new(Inventory);

		Lock(false);
		_Main.SwitchScene(0);
	}

	public void ConnectionFailed(string[] error)
	{
		_ErrorLabel.Visible = true;
		_ErrorLabel.Text = string.Join("\n", error);
		TryDisconnection();
	}

	public LoginInfo GetLoginInfo() => new(Port, Slot, Address, Password);

	// Called via CallDeferred when the client reports connection lost
	public void OnUnexpectedDisconnect()
	{
		// If user manually disconnected earlier, don't start reconnect
		if (_manualDisconnect) return;

		// Switch back to login screen immediately
		_Main.SwitchScene(0);

		// Clear previous error label
		_ErrorLabel.Visible = true;
		_ErrorLabel.Text = "Disconnected. Trying to reconnect...";

		// ensure only one reconnect loop
		if (_reconnectRunning) return;
		_reconnectRunning = true;

	// Send a message to the webhook announcing AP disconnect
	if (!string.IsNullOrWhiteSpace(WebhookManager.Config?.WebhookUrl))
	{
		_ = DiscordWebhook.SendMessageAsync(WebhookManager.Config.WebhookUrl, "Lost connection to Archipelago server");
	}

		_ = ReconnectLoopAsync();
	}

	private async Task ReconnectLoopAsync()
	{
		// sequence generator (see note in comment below)
		var attempts = BuildBackoffSequence();
		foreach (var waitSeconds in attempts)
		{
			if (_manualDisconnect) break; // stop attempting if user disconnected manually
			// attempt now
			for (int i = waitSeconds; i > 0; i--)
			{
				if (_manualDisconnect) break;

				CallDeferred("_ShowReconnectCountdown", i);
				await Task.Delay(1000);
			}
			if (_manualDisconnect) break;
			// one-attempt: mirror TryConnection's connect logic, but synchronous from this async method
			CallDeferred("Lock", true);
			ApClient attemptClient = new ApClient();
			string[] error = null;
			try
			{
				lock (attemptClient)
				{
					error = attemptClient.TryConnect(GetLoginInfo(), 0x0AF5F0AC,
					"DeathLinkipelago", AllItems,
					tags: ["DeathLink"]);
				}
			}
			catch (Exception e)
			{
				error = new string[] { e.Message };
			}
			if (error is null || error.Length == 0)
			{
				// success: assign and wire up like TryConnection did
				Client = attemptClient;
				Client.OnConnectionLost += (_, _) => CallDeferred("OnUnexpectedDisconnect");
				CallDeferred("Connected");
				CallDeferred("Lock", false);
				_reconnectRunning = false;
				_ErrorLabel.Visible = false;

				// Announce reconnection to webhook
				if (!string.IsNullOrWhiteSpace(WebhookManager.Config?.WebhookUrl))
				{
					_ = DiscordWebhook.SendMessageAsync(WebhookManager.Config.WebhookUrl, "Reconnected to Archipelago server");
				}
				return;
			}
			else
			{
				// failure: show message but continue
				CallDeferred("_ShowReconnectError", error);
			}
			CallDeferred("Lock", false);
			// continue to next backoff delay
		}
		// ended attempts without success (or manual disconnect)
		_reconnectRunning = false;

		if (!_manualDisconnect)
		{
			_ErrorLabel.Text = "Unable to reconnect. Please check server or network.";
		}
	}

	public void _ShowReconnectCountdown(int secondsLeft)
	{
		_ErrorLabel.Visible = true;
		_ErrorLabel.Text = $"Disconnected. Trying to reconnect in {secondsLeft}s...";
	}

	public void _ShowReconnectError(string[] error)
	{
		_ErrorLabel.Visible = true;
		_ErrorLabel.Text = string.Join("\n", error);
	}

	/// Build a backoff sequence.
	/// small repeats early (5s,10s,15s...), then longer repeated waits (600s,1800s,3600s...).
	/// This generator creates a sequence with many repeats of large intervals so reconnection
	/// keeps trying for a long time but still respects single-attempt-per-cycle.
	private List<int> BuildBackoffSequence()
	{
		var seq = new List<int>();
		// early quick repeats
		seq.AddRange(new int[] { 5, 5, 10, 10, 15, 15, 30, 30, 60, 60 });
		// medium
		seq.AddRange(new int[] { 120, 120, 120, 120, 120 }); // repeat 120 a few times
		// longer
		for (int i = 0; i < 6; i++) seq.Add(600);   // 10 minutes repeated 6 times
		for (int i = 0; i < 6; i++) seq.Add(1800);  // 30 minutes repeated 6 times
		for (int i = 0; i < 120; i++) seq.Add(3600); // 1 hour repeated many times
		return seq;
	}
}
