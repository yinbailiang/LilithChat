using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using Il2CppUI.TraySetting;
using MelonLoader;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Networking;

[assembly: MelonInfo(typeof(LilithChat.Core), "LilithChat", "1.0.0", "YinBailiang", null)]
[assembly: MelonGame("Nino", "Lilith")]

namespace LilithChat
{

	[HarmonyPatch(typeof(TraySettingView), "Awake")]
	public static class TraySettingViewPatch
	{

		private static void Postfix(TraySettingView __instance)
		{
			MelonCoroutines.Start(AddCustomControlsDelayed(__instance));
		}

		/// <summary>
		/// 从 source 复制一个配置行：禁用本地化标签 + 设置标签文本 + 初始化输入框及其 onEndEdit 回调
		/// </summary>
		private static Transform CreateConfigRow(Transform source, Transform parent, string name, string labelText, string initialValue, System.Action<string> onEndEdit)
		{
			Transform row = UnityEngine.Object.Instantiate(source, parent);
			row.name = name;

			var label = row.FindChild("Text (TMP)");
			if (label != null)
			{
				var localize = label.GetComponent<LocalizeStringEvent>();
				if (localize != null) localize.enabled = false;
				var tmp = label.GetComponent<TextMeshProUGUI>();
				if (tmp != null) tmp.text = labelText;
			}

			var inputGo = row.FindChild("InputField (TMP)");
			if (inputGo != null)
			{
				var input = inputGo.GetComponent<TMP_InputField>();
				input.text = initialValue;
				input.onEndEdit.RemoveAllListeners();
				input.onEndEdit.AddListener(onEndEdit);
			}

			return row;
		}

		private static IEnumerator<object> AddCustomControlsDelayed(TraySettingView view)
		{
			yield return null; // 等待一帧

			// 查找父容器（优先用 ContentRoot，否则用自身 transform）
			Transform parent = view.transform.Find("Root/Bg/vertical");
			if (parent == null)
			{
				MelonLogger.Error("Root/Bg/vertical not found, aborting UI injection.");
				yield break;
			}

			Transform source = parent.Find("musicDirInput");
			if (source == null)
			{
				MelonLogger.Error("musicDirInput not found, aborting UI injection.");
				yield break;
			}

			Core core = Melon<Core>.Instance;

			if (parent.FindChild("llmModelName") == null)
			{
				Transform model_id = CreateConfigRow(source, parent.transform, "llmModelName", "Model Name", core.llmModelName.Value,
					value =>
					{
						if (value == core.llmModelName.Value) return;
						core.llmModelName.Value = value;
						core.SavePreferences();
						MelonLogger.Msg($"MODEL NAME 已更新: {value}");
					}
				);
				view._rowTabMap[model_id] = TraySettingView.TabLilith;
				view._rowInitialActive[model_id] = true;
			}

			if (parent.FindChild("apiUrl") == null)
			{
				Transform api_url = CreateConfigRow(source, parent.transform, "apiUrl", "Api Url", core.apiUrl.Value,
					value =>
					{
						if (value == core.apiUrl.Value) return;
						core.apiUrl.Value = value;
						core.SavePreferences();
						MelonLogger.Msg($"API URL 已更新: {value}");
					}
				);
				view._rowTabMap[api_url] = TraySettingView.TabLilith;
				view._rowInitialActive[api_url] = true;
			}

			if (parent.FindChild("apiKey") == null)
			{
				Transform api_key = CreateConfigRow(source, parent.transform, "apiKey", "Api Key", core.apiKey.Value,
					value =>
					{
						if (value == core.apiKey.Value) return;
						core.apiKey.Value = value;
						core.SavePreferences();
						MelonLogger.Msg($"API KEY 已更新: {value}");
					}
				);
				view._rowTabMap[api_key] = TraySettingView.TabLilith;
				view._rowInitialActive[api_key] = true;
			}

			if (parent.FindChild("promptFilePath") == null)
			{
				Transform prompt_path = CreateConfigRow(source, parent.transform, "promptFilePath", "Prompt File Path", core.promptFilePath.Value,
					value =>
					{
						if (value == core.promptFilePath.Value) return;
						if (!File.Exists(value))
						{
							
							MelonLogger.Msg($"PROMPT FILE PATH 指向的文件不存在: {value}");
							if (DialogueManagerPatch.Instance != null){
								DialogueManagerPatch.Instance.Say("我找不到你说的那个文件。");
							}
							return;
						}
						core.promptFilePath.Value = value;
						core.SavePreferences();
						MelonLogger.Msg($"PROMPT FILE PATH 已更新: {value}");
					}
				);
				view._rowTabMap[prompt_path] = TraySettingView.TabLilith;
				view._rowInitialActive[prompt_path] = true;
			}
		}
	};


	[HarmonyPatch(typeof(PlayerLineController), "Awake")]
	public static class PlayerLineControllerAwakePatch
	{
		public static PlayerLineController Instance = null;
		private static void Postfix(PlayerLineController __instance)
		{
			PlayerLineControllerAwakePatch.Instance = __instance;
			var states = new Il2CppSystem.Collections.Generic.List<string>();
			states.Add("Menu");
			states.Add("AiCustom");
			__instance._database.entries.Add(new PlayerLineEntry
			{
				id = 50001,
				LineID = 50001,
				groupId = 0,
				playerStates = states,
				viewLimit = 1,
				text = "聊聊天",
			});
		}

	};

	[HarmonyPatch(typeof(PlayerLineController), "OnButtonClicked")]
	public static class PlayerLineControllerPatch
	{
		private static bool Prefix(PlayerLineController __instance, int index)
		{
			if (index >= __instance._currentEntries.Count) return true;
			if (__instance._isCustomMode) return true;
			var entry = __instance._currentEntries[index];
			if (entry.playerStates.Contains("AiCustom"))
			{
				PlayerLineController.MarkEntryPicked(entry);
				__instance.Hide();
				entry.viewLimit = 1;
				MelonCoroutines.Start(Agent.LLMTalk("我想随便聊聊。"));
				return false;
			}
			return true;
		}

	};

	[HarmonyPatch(typeof(DialogueManager), "Awake")]
	public static class DialogueManagerPatch
	{
		public static DialogueManager Instance = null;
		private static void Postfix(DialogueManager __instance)
		{
			DialogueManagerPatch.Instance = __instance;
		}

	};


	public static class Agent
	{

		public static IEnumerator<object> LLMTalk(string userMessage)
		{
			Core core = Melon<Core>.Instance;

			var payload = new
			{
				model = core.llmModelName.Value,
				messages = new[] {
					new { role = "system", content = core.prompt },
					new { role = "user", content = userMessage }
				}
			};
			string json = JsonSerializer.Serialize(payload);

			UnityWebRequest request = new UnityWebRequest(core.apiUrl.Value, "POST");
			byte[] bodyRaw = UTF8Encoding.UTF8.GetBytes(json);
			request.uploadHandler = new UploadHandlerRaw(bodyRaw);
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("Authorization", "Bearer " + core.apiKey.Value);

			yield return request.SendWebRequest();

			if (request.result != UnityWebRequest.Result.Success)
			{
				request.Dispose();
				MelonLogger.Error("请求失败: " + request.error);
				yield break;
			}

			string responseText = request.downloadHandler.text;
			MelonLogger.Msg("LLM 回复: " + responseText);

			using JsonDocument doc = JsonDocument.Parse(responseText);
			JsonElement root = doc.RootElement;

			// 安全取出 choices[0].message.content
			if (root.TryGetProperty("choices", out JsonElement choices) &&
				choices.GetArrayLength() > 0)
			{
				JsonElement firstChoice = choices[0];
				if (firstChoice.TryGetProperty("message", out JsonElement message) &&
					message.TryGetProperty("content", out JsonElement content))
				{
					string reply = content.GetString();
					DialogueManagerPatch.Instance.Say(reply);
				}
			}

			request.Dispose();
		}
	}

	public class Core : MelonMod
	{

		public readonly static string ConfigPath = "UserData/LilithChat";

		public string prompt = null;

		private MelonPreferences_Category _llmCategory;
		public MelonPreferences_Entry<string> llmModelName;
		public MelonPreferences_Entry<string> apiKey;
		public MelonPreferences_Entry<string> apiUrl;
		public MelonPreferences_Entry<string> promptFilePath;
		public void SavePreferences()
		{
			this._llmCategory.SaveToFile();
		}
		public override void OnInitializeMelon()
		{
			_llmCategory = MelonPreferences.CreateCategory("LilithChat", "LilithChat 配置");

			llmModelName = _llmCategory.CreateEntry("ModelName", "XXXX", "MODEL NAME");
			apiKey = _llmCategory.CreateEntry("APIKey", "sk-XXXXXXX", "API KEY");
			apiUrl = _llmCategory.CreateEntry("APIUrl", "https://api.openai.com/v1/chat/completions", "API URL");
			promptFilePath = _llmCategory.CreateEntry("PromptFilePath", "XXX/XXX/prompt.md", "提示词预设");

			if (!Directory.Exists(Core.ConfigPath))
			{
				Directory.CreateDirectory(Core.ConfigPath);
			}

			_llmCategory.SetFilePath("UserData/LilithChat/LilithChat.cfg");

			if (File.Exists(promptFilePath.Value))
			{
				prompt = File.ReadAllText(promptFilePath.Value, encoding: Encoding.UTF8);
			}

			promptFilePath.OnEntryValueChanged.Subscribe((str_old, str_new) =>
			{
				if (File.Exists(str_new))
				{
					prompt = File.ReadAllText(str_new, encoding: Encoding.UTF8);
				}
			});

			LoggerInstance.Msg("LilthChat initialized.");
		}

	};
}