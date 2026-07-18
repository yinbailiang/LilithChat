using HarmonyLib;
using Il2Cpp;
using Il2CppCysharp.Threading.Tasks;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using Il2CppUI.TraySetting;
using MelonLoader;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Networking;

[assembly: MelonInfo(typeof(LilithChat.Core), "LilithChat", "1.0.0", "YinBailiang", null)]
[assembly: MelonGame("Nino", "Lilith")]

namespace LilithChat {

	[HarmonyPatch(typeof(TraySettingView), "Awake")]
	public static class TraySettingViewPatch {

		private static void Postfix(TraySettingView __instance) {
			MelonCoroutines.Start(AddCustomControlsDelayed(__instance));
		}

		private static IEnumerator<object> AddCustomControlsDelayed(TraySettingView view) {
			yield return null; // 等待一帧

			// 查找父容器（优先用 ContentRoot，否则用自身 transform）
			Transform parent = view.transform.Find("Root/Bg/vertical");
			if (parent == null) {
				MelonLogger.Error("Root/Bg/vertical not found, aborting UI injection.");
				yield break;
			}

			Transform source = parent.Find("yourName");
			if (source == null) {
				MelonLogger.Error("yourName not found, aborting UI injection.");
				yield break;
			}

			Core core = Melon<Core>.Instance;

			Transform model_id = UnityEngine.Object.Instantiate(source, parent.transform);
			model_id.name = "llmModelName";
			model_id.FindChild("Text (TMP)").GetComponent<LocalizeStringEvent>().enabled = false;
			model_id.FindChild("Text (TMP)").GetComponent<TextMeshProUGUI>().text = "Model Name";
			model_id.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().text = core.llmModelName.Value;
			model_id.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().onEndEdit.RemoveAllListeners();
			model_id.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().onEndEdit.AddListener(
				(System.Action<string>)(value => {
					if (value == core.llmModelName.Value) {
						return;
					}
					core.llmModelName.Value = value;
					core.SavePreferences();
					MelonLogger.Msg($"MODEL NAME 已更新: {value}");
				})
			);

			Transform api_url = UnityEngine.Object.Instantiate(source, parent.transform);
			api_url.name = "apiUrl";
			api_url.FindChild("Text (TMP)").GetComponent<LocalizeStringEvent>().enabled = false;
			api_url.FindChild("Text (TMP)").GetComponent<TextMeshProUGUI>().text = "Api Url";
			api_url.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().text = core.apiUrl.Value;
			api_url.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().onEndEdit.RemoveAllListeners();
			api_url.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().onEndEdit.AddListener(
				(System.Action<string>)(value => {
					if (value == core.apiUrl.Value) {
						return;
					}
					core.apiUrl.Value = value;
					core.SavePreferences();
					MelonLogger.Msg($"API URL 已更新: {value}");
				})
			);

			Transform api_key = UnityEngine.Object.Instantiate(source, parent.transform);
			api_key.name = "apiKey";
			api_key.FindChild("Text (TMP)").GetComponent<LocalizeStringEvent>().enabled = false;
			api_key.FindChild("Text (TMP)").GetComponent<TextMeshProUGUI>().text = "Api Key";
			api_key.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().text = core.apiKey.Value;
			api_key.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().onEndEdit.RemoveAllListeners();
			api_key.FindChild("InputField (TMP)").GetComponent<TMP_InputField>().onEndEdit.AddListener(
				(System.Action<string>)(value => {
					if (value == core.apiKey.Value) {
						return;
					}
					core.apiKey.Value = value;
					core.SavePreferences();
					MelonLogger.Msg($"API KEY 已更新: {value}");
				})
			);
		}
	};


	[HarmonyPatch(typeof(PlayerLineController), "Awake")]
	public static class PlayerLineControllerAwakePatch {
		public static PlayerLineController Instance = null;
		private static void Postfix(PlayerLineController __instance) {
			PlayerLineControllerAwakePatch.Instance = __instance;
			var states = new Il2CppSystem.Collections.Generic.List<string>();
			states.Add("Menu");
			states.Add("AiCustom");
			__instance._database.entries.Add(new PlayerLineEntry {
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
	public static class PlayerLineControllerPatch {
		private static bool Prefix(PlayerLineController __instance, int index) {
			if (index >= __instance._currentEntries.Count) return true;
			if (__instance._isCustomMode) return true;
			var entry = __instance._currentEntries[index];
			if (entry.playerStates.Contains("AiCustom")) {
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
	public static class DialogueManagerPatch {
		public static DialogueManager Instance = null;
		private static void Postfix(DialogueManager __instance) {
			DialogueManagerPatch.Instance = __instance;
		}

	};


	public static class Agent {

		public static IEnumerator<object> LLMTalk(string userMessage) {
			Core core = Melon<Core>.Instance;

			var payload = new {
				model = core.llmModelName.Value,
				messages = new[] {
					new { role = "system", content = core.promptPreset.Value },
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

			if (request.result != UnityWebRequest.Result.Success) {
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
				choices.GetArrayLength() > 0) {
				JsonElement firstChoice = choices[0];
				if (firstChoice.TryGetProperty("message", out JsonElement message) &&
					message.TryGetProperty("content", out JsonElement content)) {
					string reply = content.GetString();
					DialogueManagerPatch.Instance.Say(reply);
				}
			}

			request.Dispose();
		}
	}

	public class Core : MelonMod {

		// 分类实例
		private MelonPreferences_Category _llmCategory;
		// 配置条目
		public MelonPreferences_Entry<string> llmModelName;
		public MelonPreferences_Entry<string> apiKey;
		public MelonPreferences_Entry<string> apiUrl;
		public MelonPreferences_Entry<int> contextLength;
		public MelonPreferences_Entry<string> promptPreset;
		public void SavePreferences() {
			this._llmCategory.SaveToFile();
		}
		public override void OnInitializeMelon() {
			_llmCategory = MelonPreferences.CreateCategory("LilithChat", "LilithChat 配置");

			llmModelName = _llmCategory.CreateEntry("ModelName", "XXXX", "MODEL NAME");
			apiKey = _llmCategory.CreateEntry("APIKey", "sk-XXXXXXX", "API KEY");
			apiUrl = _llmCategory.CreateEntry("APIUrl", "https://api.openai.com/v1/chat/completions", "API URL");
			contextLength = _llmCategory.CreateEntry("ContextLength", 4096, "上下文长度");
			promptPreset = _llmCategory.CreateEntry("PromptPreset", "You are a helpful assistant.", "提示词预设");

			_llmCategory.SetFilePath("UserData/LilithChat.cfg");

			LoggerInstance.Msg("LilthChat initialized.");
		}

	};
}