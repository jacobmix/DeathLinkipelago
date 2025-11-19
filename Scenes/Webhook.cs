using Godot;
using DeathLinkipelago.Scripts;

public partial class Webhook : Control
{
	// --- WEBHOOK URL ---
	private LineEdit _url;	  // Webhook URL field.

	// --- ITEMS ---
	private CheckBox _prog;	 // Progression
	private CheckBox _useful;   // Useful / Non-Progression
	private CheckBox _trap;	 // Trap
	private CheckBox _junk;	 // Junk
	private CheckBox _excluded; // Excluded
	private CheckBox _hints;	// Hints

	// --- COMPLETION ---
	private CheckBox _goal;	 // Player's goal completed
	private CheckBox _finish;   // Multiworld/Team finished

	// --- PLAYER ACTIVITY ---
	private CheckBox _join;	 // Join/Leave/Tag change
	private CheckBox _chat;	 // Chat messages

	// --- DEATHLINK ---
	private CheckBox _deaths;   // Death/DeathLink

	// --- WEBHOOK CONNECTION ---
	private Button _connect;	// Webhook connect/disconnect button.
	private Label _status;	  // Webhook connection status label.

	private bool _ready = false;
	private bool _manualStatusUpdate = false;

	public override void _Ready()
	{
		WebhookManager.Initialize(this);

		// --- WEBHOOK URL ---
		_url = GetNode<LineEdit>("WebhookUrlInput");

		// --- ITEMS ---
		_prog	= GetNode<CheckBox>("FlowContainer/SendProgressionCheck");
		_useful  = GetNode<CheckBox>("FlowContainer/SendUsefulCheck");
		_trap	= GetNode<CheckBox>("FlowContainer/SendTrapCheck");
		_junk	= GetNode<CheckBox>("FlowContainer/SendJunkCheck");
		_excluded= GetNode<CheckBox>("FlowContainer/SendExcludedCheck");
		_hints   = GetNode<CheckBox>("FlowContainer/SendHintsCheck");

		// --- COMPLETION ---
		_goal   = GetNode<CheckBox>("FlowContainer/SendGoalCheck");
		_finish = GetNode<CheckBox>("FlowContainer/SendFinishCheck");

		// --- PLAYER ACTIVITY ---
		_join = GetNode<CheckBox>("FlowContainer/SendJoinLeaveCheck");
		_chat = GetNode<CheckBox>("FlowContainer/SendChatCheck");

		// --- DEATHLINK ---
		_deaths = GetNode<CheckBox>("FlowContainer/SendDeathsCheck");

		// --- WEBHOOK CONNECTION ---
		_connect = GetNode<Button>("ConnectButton");
		_status  = GetNode<Label>("StatusLabel");

		LoadUiFromConfig();
		_connect.Pressed += OnConnectPressed;

		// --- Hook webhook reconnect status updates ---
		WebhookManager.StatusUpdate = status =>
		{
			_manualStatusUpdate = true;
			_status.Text = status;
			_status.Modulate = Colors.OrangeRed; // show "disconnected / reconnecting" as orange
		};

		_ready = true;
		UpdateUiState();
	}

	private void LoadUiFromConfig()
	{
		var cfg = WebhookManager.Config;

		// --- WEBHOOK URL ---
		_url.Text = cfg.WebhookUrl ?? "";

		// --- ITEMS ---
		_prog.ButtonPressed	 = cfg.SendProgression;
		_useful.ButtonPressed   = cfg.SendUseful;
		_trap.ButtonPressed	 = cfg.SendTrap;
		_junk.ButtonPressed	 = cfg.SendJunk;
		_excluded.ButtonPressed = cfg.SendExcluded;
		_hints.ButtonPressed	= cfg.SendHints;

		// --- COMPLETION ---
		_goal.ButtonPressed   = cfg.SendGoal;
		_finish.ButtonPressed = cfg.SendFinish;

		// --- PLAYER ACTIVITY ---
		_join.ButtonPressed = cfg.SendJoinLeave;
		_chat.ButtonPressed = cfg.SendChat;

		// --- DEATHLINK ---
		_deaths.ButtonPressed = cfg.SendDeaths;
	}

	private void SaveUiToConfig()
	{
		var cfg = WebhookManager.Config;

		// --- WEBHOOK URL ---
		cfg.WebhookUrl = _url.Text.Trim();

		// --- ITEMS ---
		cfg.SendProgression = _prog.ButtonPressed;
		cfg.SendUseful	  = _useful.ButtonPressed;
		cfg.SendTrap		= _trap.ButtonPressed;
		cfg.SendJunk		= _junk.ButtonPressed;
		cfg.SendExcluded	= _excluded.ButtonPressed;
		cfg.SendHints	   = _hints.ButtonPressed;

		// --- COMPLETION ---
		cfg.SendGoal   = _goal.ButtonPressed;
		cfg.SendFinish = _finish.ButtonPressed;

		// --- PLAYER ACTIVITY ---
		cfg.SendJoinLeave = _join.ButtonPressed;
		cfg.SendChat	  = _chat.ButtonPressed;

		// --- DEATHLINK ---
		cfg.SendDeaths = _deaths.ButtonPressed;

		WebhookManager.SaveSettings();
	}

	private async void OnConnectPressed()
	{
		if (!WebhookManager.IsConnected)
		{
			SaveUiToConfig();
			bool ok = await WebhookManager.ConnectAsync();

			if (!ok)
			{
				_status.Text = "Status: Failed to Connect";
				_status.Modulate = Colors.OrangeRed;
			}
		}
		else
		{
			WebhookManager.Disconnect();
		}

		UpdateUiState();
	}

	public void UpdateUiState()
	{
		bool connected = WebhookManager.IsConnected;

		_url.Editable = !connected;

		// Disable all checkboxes while connected
		foreach (var cb in new[] {
				_prog, _useful, _trap, _junk, _excluded, _hints,
				_goal, _finish,
				_join, _chat,
				_deaths
			})
			{
				cb.Disabled = connected;
			}

			_connect.Text = connected ? "Disconnect Webhook" : "Connect Webhook";

			// Only overwrite status if not manually set by reconnect
			if (!_manualStatusUpdate)
			{
				if (connected)
				{
					_status.Text = "Status: Connected";
					_status.Modulate = Colors.LightGreen;
				}
				else
				{
					_status.Text = "Status: Disconnected";
					_status.Modulate = Colors.Red;
				}
			}

			// Reset manual flag if connected
			if (connected)
				_manualStatusUpdate = false;
		}

	public override void _Process(double delta)
	{
		if (!_ready) return;
		UpdateUiState();
	}
}
