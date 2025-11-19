using System;
using System.IO;
using Godot;
using Newtonsoft.Json;

namespace DeathLinkipelago.Scripts
{
	public class Config
	{
		public long SecondsPerLifeCoin;
		public long DeathCheckAmount;
		public bool SendTrapsAfterGoal;
		public bool HasFunnyButton;
		public bool UseGlobalCounter;
		public bool SendScoutHints;

		// WEBHOOK
		public string WebhookUrl { get; set; } = "";
		public bool SendHints { get; set; } = true;
		public bool SendProgression { get; set; } = true;
		public bool SendUseful { get; set; } = true;
		public bool SendTrap { get; set; } = true;
		public bool SendJunk { get; set; } = true;
		public bool SendExcluded { get; set; } = true;
		public bool SendChat { get; set; } = false;
		public bool SendJoinLeave { get; set; } = false;
		public bool SendGoal { get; set; } = true;
		public bool SendFinish { get; set; } = true;
		public bool SendDeaths { get; set; } = true;

		public Config(long seconds, long amount, bool traps, bool funny, bool global, bool scout)
		{
			SecondsPerLifeCoin = seconds;
			DeathCheckAmount = amount;
			SendTrapsAfterGoal = traps;
			HasFunnyButton = funny;
			UseGlobalCounter = global;
			SendScoutHints = scout;

			// Initialize webhook defaults
			WebhookUrl = "";
			SendHints = true;
			SendProgression = true;
			SendUseful = true;
			SendTrap = true;
			SendJunk = true;
			SendExcluded = true;
			SendChat = false;
			SendJoinLeave = false;
			SendGoal = true;
			SendFinish = true;
			SendDeaths = true;
		}

		public void Save()
		{
			try
			{
				var savePath = $"{MainController.SaveDir}/config.json";
				var json = JsonConvert.SerializeObject(this, Formatting.Indented);
				File.WriteAllText(savePath, json);
			}
			catch (Exception e)
			{
				GD.PrintErr($"Failed to save config: {e.Message}");
			}
		}

		public static Config LoadConfig()
		{
			var path = $"{MainController.SaveDir}/config.json";
			if (File.Exists(path))
			{
				try
				{
					var json = File.ReadAllText(path);
					return JsonConvert.DeserializeObject<Config>(json);
				}
				catch (Exception e)
				{
					GD.PrintErr($"Failed to load config: {e.Message}");
				}
			}

			// default config
			return new Config(60, 10, false, false, false, false);
		}
	}
}
