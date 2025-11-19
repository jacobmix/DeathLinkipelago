using Godot;
using System;
using System.Threading.Tasks;

namespace DeathLinkipelago.Scripts
{
	public static class WebhookManager
	{
		private const string ConfigPath = "user://webhook.cfg";

		public static WebhookConfig Config { get; private set; } = new();

		public static bool IsConnected { get; private set; } = false;

		private static HttpRequest _http;

		// flags for reconnect behavior
		private static bool _manualWebhookDisconnect = false; // set when user clicks disconnect
		private static bool _webhookReconnectRunning = false; // ensure single reconnect loop

		public static Action<string> StatusUpdate;

		public static void Initialize(Node parent)
		{
			if (_http == null)
			{
				_http = new HttpRequest();
				parent.AddChild(_http);
			}

			LoadSettings();
		}

		// -------------------------------------------------------------
		//  CONNECTION
		// -------------------------------------------------------------
		public static async Task<bool> ConnectAsync()
		{
			GD.Print("ConnectAsync called");

			// calling ConnectAsync means a non-manual connect attempt -> clear manual flag
			_manualWebhookDisconnect = false;

			if (string.IsNullOrWhiteSpace(Config.WebhookUrl))
			{
				GD.Print("Webhook URL is empty!");
				return false;
			}

			if (_http == null)
			{
				GD.Print("HttpRequest node (_http) is null!");
				return false;
			}

			try
			{
				GD.Print($"Attempting to connect to {Config.WebhookUrl}");
				var result = await SendAsync("Webhook connected.");
				GD.Print($"SendAsync returned: {result}");
				if (!result)
					return false;
			}
			catch (Exception e)
			{
				GD.PrintErr($"ConnectAsync exception: {e}");
				return false;
			}

			IsConnected = true;
			GD.Print("Webhook connected successfully!");
			return true;
		}

		/// Low-level send used by connect to test webhook. Returns success/failure.
		/// This will not start reconnection - ConnectAsync handles flags.
		public static async Task<bool> SendRawAsync(string text)
		{
			if (!IsConnected)
			{
				GD.Print("SendRawAsync called but not connected");
			}

			if (_http == null)
			{
				GD.Print("SendRawAsync: HttpRequest node is null!");
				return false;
			}

			var json = "{\"content\":\"" + text.Replace("\"", "\\\"") + "\"}";

			GD.Print($"Sending HTTP request: {json}");

			var tcs = new TaskCompletionSource<bool>();

			void HandleRequestCompleted(long result, long response_code, string[] headers, byte[] body)
			{
				GD.Print($"Request completed: response_code={response_code}");
				bool ok = response_code >= 200 && response_code < 300;
				tcs.TrySetResult(ok);
				_http.RequestCompleted -= HandleRequestCompleted;
			}

			_http.RequestCompleted += HandleRequestCompleted;

			_http.Request(
				Config.WebhookUrl,
				new[] { "Content-Type: application/json" },
				HttpClient.Method.Post,
				json
			);

			return await tcs.Task;
		}

		/// Public send entry used by the rest of the app. If it fails and it's been connected,
		/// mark disconnected and possibly start a reconnect loop (unless manual disconnect).
		public static async Task<bool> SendAsync(string text)
		{

			// if we were never connected, attempt to connect only if not a manual disconnect
			// but keep behavior simple: try to send regardless and react to failure
			var success = await SendRawAsync(text);
			if (!success)
			{
				// mark disconnected
				if (IsConnected) GD.Print("Webhook appears to have become disconnected.");
				IsConnected = false;
				// if webhook was not manually disconnected, and AP is connected, start reconnect attempts
				if (!_manualWebhookDisconnect &&
					MainController.Client is not null &&
					MainController.Client.IsConnected &&
					!_webhookReconnectRunning)
				{
					_webhookReconnectRunning = true;
					_ = StartWebhookReconnectAsync();
				}
			}
			else
			{
				// succeeded
				IsConnected = true;
			}

			return success;
		}

		public static async void Disconnect()
		{
			_manualWebhookDisconnect = true;

			// Announce manual disconnect to webhook (fire-and-forget)
			if (!string.IsNullOrWhiteSpace(Config.WebhookUrl))
			{
				await SendRawAsync("Webhook is disconnecting manually...");
			}

			IsConnected = false;
			GD.Print("Webhook disconnected.");
		}

		// Try reconnecting with backoff. Will stop if _manualWebhookDisconnect becomes true,
		// or if AP client becomes disconnected (no point trying).
		private static async Task StartWebhookReconnectAsync()
		{
			try
			{
				var seq = BuildBackoffSequence();
				foreach (var wait in seq)
				{
					// only attempt if AP is connected
					if (_manualWebhookDisconnect ||
						MainController.Client is null ||
						!MainController.Client.IsConnected)
						break;

					// Countdown loop
					for (int i = wait; i > 0; i--)
					{
						StatusUpdate?.Invoke($"Disconnected. Trying to reconnect in {i}s...");
						await Task.Delay(1000);

						if (_manualWebhookDisconnect ||
							MainController.Client is null ||
							!MainController.Client.IsConnected)
							break;
					}

					if (_manualWebhookDisconnect ||
						MainController.Client is null ||
						!MainController.Client.IsConnected)
						break;

					StatusUpdate?.Invoke("Attempting webhook reconnect...");
					bool ok = await ConnectAsync();
					if (ok)
					{
						StatusUpdate?.Invoke("Webhook connected!");
						_webhookReconnectRunning = false;
						return;
					}
					else
					{
						StatusUpdate?.Invoke("Webhook reconnect failed. Will retry...");
					}
				}
			}
			finally
			{
				_webhookReconnectRunning = false;
			}
		}

		// -------------------------------------------------------------
		//  SETTINGS FILE
		// -------------------------------------------------------------
		public static void LoadSettings()
		{
			var cfg = new ConfigFile();
			var err = cfg.Load(ConfigPath);

			if (err != Error.Ok)
			{
				Config = new WebhookConfig();
				return;
			}

			// Webhook URL
			Config.WebhookUrl	  = cfg.GetValue("webhook", "url", "").ToString();

			// Items
			Config.SendProgression = (bool)cfg.GetValue("webhook", "progression", true);
			Config.SendUseful	  = (bool)cfg.GetValue("webhook", "useful", true);
			Config.SendTrap		= (bool)cfg.GetValue("webhook", "trap", true);
			Config.SendJunk		= (bool)cfg.GetValue("webhook", "junk", true);
			Config.SendExcluded	= (bool)cfg.GetValue("webhook", "excluded", true);
			Config.SendHints	   = (bool)cfg.GetValue("webhook", "hints", true);

			// Completion
			Config.SendGoal		= (bool)cfg.GetValue("webhook", "goal", true);
			Config.SendFinish	  = (bool)cfg.GetValue("webhook", "finish", true);

			// Activity
			Config.SendChat		= (bool)cfg.GetValue("webhook", "chat", false);
			Config.SendJoinLeave   = (bool)cfg.GetValue("webhook", "joins", false);

			// DeathLink
			Config.SendDeaths	  = (bool)cfg.GetValue("webhook", "deaths", true);
		}

		public static void SaveSettings()
		{
			var cfg = new ConfigFile();

			// Webhook URL
			cfg.SetValue("webhook", "url", Config.WebhookUrl);

			// Items
			cfg.SetValue("webhook", "progression", Config.SendProgression);
			cfg.SetValue("webhook", "useful", Config.SendUseful);
			cfg.SetValue("webhook", "trap", Config.SendTrap);
			cfg.SetValue("webhook", "junk", Config.SendJunk);
			cfg.SetValue("webhook", "excluded", Config.SendExcluded);
			cfg.SetValue("webhook", "hints", Config.SendHints);

			// Completion
			cfg.SetValue("webhook", "goal", Config.SendGoal);
			cfg.SetValue("webhook", "finish", Config.SendFinish);

			// Activity
			cfg.SetValue("webhook", "chat", Config.SendChat);
			cfg.SetValue("webhook", "joins", Config.SendJoinLeave);

			// DeathLink
			cfg.SetValue("webhook", "deaths", Config.SendDeaths);

			cfg.Save(ConfigPath);
		}

		// Slightly different BuildBackoffSequence than Login but same spirit:
		private static System.Collections.Generic.List<int> BuildBackoffSequence()
		{
			var seq = new System.Collections.Generic.List<int>();
			seq.AddRange(new int[] { 5, 5, 10, 10, 15, 15, 30, 30, 60, 60 });
			seq.AddRange(new int[] { 120, 120, 120, 120, 120 });
			for (int i = 0; i < 6; i++) seq.Add(600);
			for (int i = 0; i < 6; i++) seq.Add(1800);
			for (int i = 0; i < 120; i++) seq.Add(3600);
			return seq;
		}
	}

	// -------------------------------------------------------------
	//  CONFIG DATA STRUCT
	// -------------------------------------------------------------
	public class WebhookConfig
	{
		public string WebhookUrl = "";

		// Items
		public bool SendProgression = true;
		public bool SendUseful = true;
		public bool SendTrap = true;
		public bool SendJunk = true;
		public bool SendExcluded = true;
		public bool SendHints = true;

		// Completion
		public bool SendGoal = true;
		public bool SendFinish = true;

		// Activity
		public bool SendChat = false;
		public bool SendJoinLeave = false;

		// DeathLink
		public bool SendDeaths = true;
	}
}
