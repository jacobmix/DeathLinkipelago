using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DeathLinkipelago.Scripts;
using Godot;

namespace DeathLinkipelago.Scripts
{
	public static class WebhookHandler
	{
		private static readonly List<string> MessageQueue = new();
		private static bool QueueRunning = false;

		// Called when a death or info message should be sent
		public static void SendDeath(string player)
		{
			if (WebhookManager.Config == null || string.IsNullOrWhiteSpace(WebhookManager.Config.WebhookUrl))
				return;
			if (!WebhookManager.Config.SendDeaths) return;

			string displayName = CleanPlayerName(player);
			int playerDeaths = MainController.DeathCounter.GetValueOrDefault(player, 0);
			int totalDeaths = MainController.DeathCounter.Values.Sum();
			int topDeaths = MainController.DeathCounter.Values.DefaultIfEmpty(0).Max();
			string topPlayer = MainController.DeathCounter.FirstOrDefault(kv => kv.Value == topDeaths).Key ?? "N/A";

			string message =
				$"{displayName} has died! Total deaths: {playerDeaths}.\n" +
				$"Total deaths in multiworld: {totalDeaths}\n" +
				$"Top player: {topPlayer} with {topDeaths} deaths";

			EnqueueMessage(message);
		}

		public static void SendInfo(string message)
		{
			if (WebhookManager.Config == null || string.IsNullOrWhiteSpace(WebhookManager.Config.WebhookUrl))
				return;

			EnqueueMessage(message);
		}

		private static void EnqueueMessage(string message)
		{
			lock (MessageQueue)
			{
				MessageQueue.Add(message);
			}

			if (!QueueRunning)
				_ = RunQueueAsync();
		}

		private static async Task RunQueueAsync()
		{
			QueueRunning = true;

			while (true)
			{
				string[] batch;
				lock (MessageQueue)
				{
					if (MessageQueue.Count == 0) break;
					batch = MessageQueue.Take(5).ToArray();
					MessageQueue.RemoveRange(0, batch.Length);
				}

				string combined = string.Join("\n", batch);
				await DiscordWebhook.SendMessageAsync(WebhookManager.Config.WebhookUrl, combined);

				await Task.Delay(2000);
			}

			QueueRunning = false;
		}

		private static string CleanPlayerName(string rawName)
		{
			// Remove slot info if present
			int parenIndex = rawName.IndexOf('(');
			if (parenIndex > 0) return rawName.Substring(0, parenIndex).Trim();
			return rawName;
		}
	}
}

/**namespace DeathLinkipelago.Scripts
{
	public static class WebhookHandler
	{
		private static readonly List<string> MessageQueue = new();
		private static bool QueueRunning = false;

		/// Called when a message from Archipelago is received.
		public static async Task ProcessMessageAsync(string type, string source, string message, int? itemFlags = null, string location = null)
		{
			// Debug: print everything to Godot console
			GD.Print($"[Webhook Debug] Type: {type}, Source: {source}, Message: {message}");

			if (WebhookManager.Config == null || string.IsNullOrWhiteSpace(WebhookManager.Config.WebhookUrl))
				return;

			string formattedMessage = null;

			switch (type.ToLower())
			{
				case "deathlink":
					if (!WebhookManager.Config.SendDeaths) return;
					formattedMessage = FormatDeathMessage(source);
					break;

				case "info":
					formattedMessage = await ProcessInfoMessage(message, itemFlags, location);
					break;

				case "chat":
					if (!WebhookManager.Config.SendChat) return;
					formattedMessage = $"{AnsiPlayer(CleanPlayerName(source))}: {message}";
					break;
			}

			if (!string.IsNullOrEmpty(formattedMessage))
			{
				lock (MessageQueue)
				{
					MessageQueue.Add(formattedMessage);
				}

				if (!QueueRunning)
					_ = RunQueueAsync();
			}
		}

		// --------------------------
		//  DEATHLINK (ANSI)
		// --------------------------
		private static string FormatDeathMessage(string source)
		{
			string displayName = AnsiPlayer(CleanPlayerName(source));

			int totalDeaths = MainController.DeathCounter.Values.Sum();
			int topDeaths = MainController.DeathCounter.Values.Max();
			string topPlayerRaw = MainController.DeathCounter.First(kv => kv.Value == topDeaths).Key;
			string topPlayer = AnsiPlayer(CleanPlayerName(topPlayerRaw));

			int playerDeaths = MainController.DeathCounter[source];

			string line1 = $"{displayName} has died! They've died a total of \u001b[2;31m\u001b[1;31m{playerDeaths} times\u001b[0m\u001b[2;31m\u001b[0m.";
			string line2 = $"Total deaths sent by everyone in the \u001b[2;33mmultiworld: {totalDeaths}\u001b[0m";
			string line3 = $"Player with the most deaths is: {topPlayer}. \u001b[2;31mWith: {topDeaths}\u001b[0m";

			return $"{line1}\n{line2}\n{line3}";
		}

		// --------------------------
		//  CLEAN PLAYER NAME + ANSI
		// --------------------------
		private static string CleanPlayerName(string rawName)
		{
			var match = Regex.Match(rawName, @"^(?<alias>.+) \((?<slot>.+)\)$");
			if (!match.Success) return rawName;

			string alias = match.Groups["alias"].Value;
			string slot = match.Groups["slot"].Value;
			return alias == slot ? alias : rawName;
		}

		private static string AnsiPlayer(string name) => $"\u001b[1;37m{name}\u001b[0m"; // bright white

		// --------------------------
		//  INFO MESSAGES (ANSI)
		// --------------------------
		private static async Task<string> ProcessInfoMessage(string message, int? itemFlags = null, string location = null)
		{
			// 1️⃣ Join/Leave/Tag messages
			if (WebhookManager.Config.SendJoinLeave)
				{
					var jlRegex = new Regex(@"^(?<player>.+?) \(.+?\) (?<action>has joined|has left|has changed tags)");
					var match = jlRegex.Match(message);
					if (match.Success)
					{
						string player = CleanPlayerName(match.Groups["player"].Value);
						string action = match.Groups["action"].Value;
						return $"{AnsiPlayer(player)} {action}";
					}
				}

			// 2️⃣ Item messages
			var itemMatch = Regex.Match(message, @"^(?<player>.+) received (?<item>.+) at (?<location>.+)");
			if (itemMatch.Success)
			{
				string rawPlayer = itemMatch.Groups["player"].Value;
				string player = CleanPlayerName(rawPlayer);
				string item = itemMatch.Groups["item"].Value;
				string loc = itemMatch.Groups["location"].Value;

				string type = GetItemTypeFromFlags(itemFlags);

				if ((type == "Progression" && WebhookManager.Config.SendProgression) ||
					(type == "Useful" && WebhookManager.Config.SendUseful) ||
					(type == "Trap" && WebhookManager.Config.SendTrap) ||
					(type == "Junk" && WebhookManager.Config.SendJunk) ||
					(type == "Excluded" && WebhookManager.Config.SendExcluded) ||
					(type == "Hint" && WebhookManager.Config.SendHints))
				{
					string ansiType = AnsiType(type);
					string ansiLocation = AnsiLocation(loc);

					return $"```ansi\n{AnsiPlayer(player)} received {ansiType}{item}\u001b[0m at {ansiLocation}\n```";
				}
			}

			// 3️⃣ Goal / Finish messages
			if ((WebhookManager.Config.SendGoal &&
				 (message.Contains("has completed their goal") || message.Contains("has released all remaining items from their world"))) ||
				(WebhookManager.Config.SendFinish &&
				 message.Contains("has completed all of their games! Congratulations!")))
			{
				var goalRegex = new Regex(@"^(?<player>.+?) \(.+?\) ");
				var match = goalRegex.Match(message);
				if (match.Success)
				{
					string player = CleanPlayerName(match.Groups["player"].Value);
					string rest = message.Substring(match.Groups["player"].Length);
					return $"{AnsiPlayer(player)}{rest}";
				}
				return message;
			}

			return null;
		}

		// --------------------------
		//  ITEM TYPE FROM FLAGS
		// --------------------------
		private static string GetItemTypeFromFlags(int? flags)
		{
			if (flags == null) return "Progression";

			return flags switch
			{
				0b001 => "Progression",
				0b010 => "Useful",
				0b100 => "Junk",
				0b011 => "Trap",
				0b101 => "Excluded",
				0b110 => "Filler",
				_ => "Progression"
			};
		}

		// --------------------------
		//  ANSI COLOR HELPERS
		// --------------------------
		private static string AnsiType(string type) => type switch
		{
			"Progression" => "\u001b[1;33m",
			"Useful" => "\u001b[1;34m",
			"Trap" => "\u001b[1;31m",
			"Filler" => "\u001b[1;32m",
			"Excluded" => "\u001b[1;30m",
			"Hint" => "\u001b[1;35m",
			_ => "\u001b[0m"
		};
		private static string AnsiLocation(string location) => $"\u001b[1;36m{location}\u001b[0m"; // Cyan

		// --------------------------
		//  MESSAGE QUEUE / BATCHING
		// --------------------------
		private static async Task RunQueueAsync()
		{
			QueueRunning = true;

			while (true)
			{
				string[] batch;
				lock (MessageQueue)
				{
					if (MessageQueue.Count == 0) break;
					batch = MessageQueue.Take(5).ToArray();
					MessageQueue.RemoveRange(0, batch.Length);
				}

				string combined = string.Join("\n", batch);
				await WebhookManager.SendAsync(combined);

				await Task.Delay(2000);
			}

			QueueRunning = false;
		}
	}
}*/
