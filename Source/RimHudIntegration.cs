using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	internal static class RimHudIntegration
	{
		const int StateVersion = 1;
		const int ManagedRowVersion = 2;
		const string RimHudPackageId = "jaxe.rimhud";
		const string RimHudConfigDirectory = "RimHUD";
		const string StateFileName = "ZombielandRimHUDIntegration.json";
		const string ContaminationNeedDefName = "Contamination";
		const string ContaminationBarColorStyle = "MainToLow";
		const string RimHudTooltipLabel = "Zombie Contamination";

		static readonly string StateFilePath = Path.Combine(GenFilePaths.ConfigFolderPath, StateFileName);
		static bool rimHudPatchesApplied;

		sealed class IntegrationState
		{
			public int version = StateVersion;
			public Dictionary<string, LayoutState> layouts = new();
		}

		sealed class LayoutState
		{
			public string path;
			public string baselineHash;
			public int rowVersion;
			public int heightDelta;
			public int heightAfterInsert;
			public bool removedBecauseDisabled;
			public bool heightRestoredBecauseDisabled;
		}

		sealed class RimHudRuntime
		{
			public string modeKey;
			public string layoutPath;
			public string configPath;
			public PropertyInfo layoutProperty;
			public FieldInfo defaultLayoutField;
			public MethodInfo layoutFromXml;
			public MethodInfo layoutToXml;
			public object heightSetting;
			public PropertyInfo heightValueProperty;
			public int currentHeight;
			public int minHeight;
			public int maxHeight;
			public int rowHeight;
		}

		public static void TryApplyForActiveGame()
		{
			if (TryGetActiveGameContaminationVisibility(out var visible) == false)
				return;
			TryApply(visible);
		}

		static bool TryGetActiveGameContaminationVisibility(out bool visible)
		{
			visible = false;
			if (Current.Game == null || ZombieSettings.GetGameSettings() == null)
				return false;
			visible = Constants.CONTAMINATION;
			return true;
		}

		static void TryApply(bool contaminationVisible)
		{
			try
			{
				if (RimHudIsLoaded() == false)
					return;
				if (TryGetRuntime(out var runtime) == false)
					return;

				var state = LoadState();
				if (state == null)
					return;

				if (TryLoadEffectiveLayoutDocument(runtime, out var layoutDocument) == false)
					return;

				if (contaminationVisible == false)
				{
					RemoveContaminationRows(runtime, state, layoutDocument);
					return;
				}

				TryApplyRuntimePatches();

				if (ContainsContaminationNeed(layoutDocument.Root))
					return;

				InsertRow(runtime, state, layoutDocument, LayoutHashWithoutContamination(layoutDocument.Root));
			}
			catch
			{
			}
		}

		static bool RimHudIsLoaded()
		{
			return LoadedModManager.RunningModsListForReading
				.Any(mod => string.Equals(mod.PackageId, RimHudPackageId, StringComparison.OrdinalIgnoreCase));
		}

		static bool TryGetRuntime(out RimHudRuntime runtime)
		{
			runtime = null;

			var configDirectory = Path.Combine(GenFilePaths.ConfigFolderPath, RimHudConfigDirectory);
			if (Directory.Exists(configDirectory) == false)
				return false;

			var themeType = AccessTools.TypeByName("RimHUD.Configuration.Theme");
			var layoutLayerType = AccessTools.TypeByName("RimHUD.Interface.Hud.Layers.LayoutLayer");
			if (themeType == null || layoutLayerType == null)
				return false;

			var dockedMode = GetStaticPropertyValue(themeType, "DockedMode");
			if (TryGetBoolSetting(dockedMode, out var docked) == false)
				return false;

			var heightSetting = GetStaticPropertyValue(themeType, docked ? "InspectPaneHeight" : "FloatingHeight");
			var heightValueProperty = AccessTools.Property(heightSetting?.GetType(), "Value");
			if (heightValueProperty == null || heightValueProperty.GetSetMethod(true) == null)
				return false;
			if (TryGetIntProperty(heightSetting, "Value", out var currentHeight) == false)
				return false;
			var minHeight = 0;
			_ = TryGetIntProperty(heightSetting, "Min", out minHeight);
			if (TryGetIntProperty(heightSetting, "Max", out var maxHeight) == false)
				return false;

			var rowHeight = Mathf.CeilToInt(Text.LineHeight + 2f);
			if (rowHeight <= 0)
				return false;

			var modeKey = docked ? "Docked" : "Floating";
			var layoutPath = Path.Combine(configDirectory, modeKey + ".xml");
			var configPath = Path.Combine(configDirectory, "Config.xml");
			if (File.Exists(layoutPath) == false || File.Exists(configPath) == false)
				return false;

			var layoutProperty = AccessTools.Property(layoutLayerType, modeKey);
			var defaultLayoutField = AccessTools.Field(layoutLayerType, docked ? "DefaultDocked" : "DefaultFloating");
			var layoutFromXml = AccessTools.Method(layoutLayerType, "FromXml", new[] { typeof(XElement) });
			var layoutToXml = AccessTools.Method(layoutLayerType, "ToXml", new[] { typeof(string), typeof(int), typeof(int), typeof(int) });
			if (layoutProperty == null || layoutProperty.GetSetMethod(true) == null || defaultLayoutField == null || layoutFromXml == null || layoutToXml == null)
				return false;

			runtime = new RimHudRuntime
			{
				modeKey = modeKey,
				layoutPath = layoutPath,
				configPath = configPath,
				layoutProperty = layoutProperty,
				defaultLayoutField = defaultLayoutField,
				layoutFromXml = layoutFromXml,
				layoutToXml = layoutToXml,
				heightSetting = heightSetting,
				heightValueProperty = heightValueProperty,
				currentHeight = currentHeight,
				minHeight = minHeight,
				maxHeight = maxHeight,
				rowHeight = rowHeight
			};
			return true;
		}

		static object GetStaticPropertyValue(Type type, string name)
		{
			return AccessTools.Property(type, name)?.GetValue(null, null);
		}

		static bool TryGetBoolSetting(object setting, out bool value)
		{
			value = false;
			var property = AccessTools.Property(setting?.GetType(), "Value");
			if (property == null)
				return false;
			var raw = property.GetValue(setting, null);
			if (raw is bool boolValue)
			{
				value = boolValue;
				return true;
			}
			return false;
		}

		static bool TryGetIntProperty(object target, string propertyName, out int value)
		{
			value = 0;
			var property = AccessTools.Property(target?.GetType(), propertyName);
			if (property == null)
				return false;
			var raw = property.GetValue(target, null);
			if (raw == null)
				return false;
			value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
			return true;
		}

		static IntegrationState LoadState()
		{
			if (File.Exists(StateFilePath) == false)
				return new IntegrationState();

			var state = JsonConvert.DeserializeObject<IntegrationState>(File.ReadAllText(StateFilePath));
			if (state == null || state.version != StateVersion || state.layouts == null)
				return null;
			return state;
		}

		static bool TryLoadEffectiveLayoutDocument(RimHudRuntime runtime, out XDocument document)
		{
			document = null;

			var fileDocument = XDocument.Load(runtime.layoutPath);
			if (fileDocument.Root == null)
				return false;
			if (fileDocument.Root.Elements().Any())
			{
				document = fileDocument;
				return true;
			}

			if (TrySerializeLayout(runtime, runtime.layoutProperty.GetValue(null, null), out document) && document.Root.Elements().Any())
				return true;

			return TrySerializeLayout(runtime, runtime.defaultLayoutField.GetValue(null), out document) && document.Root.Elements().Any();
		}

		static bool TrySerializeLayout(RimHudRuntime runtime, object layout, out XDocument document)
		{
			document = null;
			if (layout == null)
				return false;
			var root = runtime.layoutToXml.Invoke(layout, new object[] { null, -1, -1, -1 }) as XElement;
			if (root == null)
				return false;
			document = new XDocument(new XElement(root));
			return document.Root != null;
		}

		static void InsertRow(RimHudRuntime runtime, IntegrationState state, XDocument layoutDocument, string baselineHash)
		{
			var healthRow = FindHealthRow(layoutDocument.Root);
			if (healthRow == null)
				return;

			var configDocument = XDocument.Load(runtime.configPath);
			if (configDocument.Root == null)
				return;

			_ = state.layouts.TryGetValue(runtime.modeKey, out var remembered);
			var expandHeight = ShouldExpandHeightForInsert(runtime, remembered);
			var newHeight = expandHeight ? runtime.currentHeight + runtime.rowHeight : runtime.currentHeight;
			if (expandHeight && newHeight > runtime.maxHeight)
				return;

			healthRow.AddAfterSelf(new XElement("Row",
				new XElement("Need",
					new XAttribute("DefName", ContaminationNeedDefName),
					new XAttribute("BarColorStyle", ContaminationBarColorStyle))));

			if (expandHeight)
				UpdateHeightConfig(configDocument.Root, runtime.modeKey, newHeight);

			var newLayout = runtime.layoutFromXml.Invoke(null, new object[] { new XElement(layoutDocument.Root) });
			if (newLayout == null)
				return;

			state.layouts[runtime.modeKey] = new LayoutState
			{
				path = runtime.layoutPath,
				baselineHash = baselineHash,
				rowVersion = ManagedRowVersion,
				heightDelta = expandHeight ? runtime.rowHeight : remembered?.heightDelta ?? 0,
				heightAfterInsert = expandHeight ? newHeight : remembered?.heightAfterInsert ?? 0,
				removedBecauseDisabled = false
			};
			var writes = new Dictionary<string, string>
			{
				{ runtime.layoutPath, ToXml(layoutDocument) },
				{ StateFilePath, SerializeState(state) }
			};
			if (expandHeight)
				writes.Add(runtime.configPath, ToXml(configDocument));

			object oldLayout = null;
			object oldHeight = null;
			try
			{
				oldLayout = runtime.layoutProperty.GetValue(null, null);
				oldHeight = runtime.heightValueProperty.GetValue(runtime.heightSetting, null);

				WriteTextTransaction(writes, () =>
				{
					if (expandHeight)
						SetHeightValue(runtime, newHeight);
					runtime.layoutProperty.SetValue(null, newLayout, null);
				});
			}
			catch
			{
				TryRestoreRuntime(runtime, oldLayout, oldHeight);
			}
		}

		static XElement FindHealthRow(XElement root)
		{
			return root
				.Descendants()
				.FirstOrDefault(element => IsRow(element) && element.Elements().Any(child => child.Name.LocalName == "Health"));
		}

		static bool ShouldExpandHeightForInsert(RimHudRuntime runtime, LayoutState remembered)
		{
			if (remembered?.rowVersion >= ManagedRowVersion
				&& remembered.heightDelta > 0
				&& remembered.heightAfterInsert > 0
				&& runtime.currentHeight >= remembered.heightAfterInsert)
				return false;
			return true;
		}

		static void RemoveContaminationRows(RimHudRuntime runtime, IntegrationState state, XDocument layoutDocument)
		{
			try
			{
				var needs = FindContaminationNeeds(layoutDocument.Root).ToList();
				if (needs.Count == 0)
					return;

				var baselineHash = LayoutHashWithoutContamination(layoutDocument.Root);
				var configDocument = XDocument.Load(runtime.configPath);
				if (configDocument.Root == null)
					return;

				RemoveContaminationNeeds(layoutDocument.Root);

				var newHeight = runtime.currentHeight;
				var hasRemembered = state.layouts.TryGetValue(runtime.modeKey, out var remembered) && remembered != null;
				var shrinkHeight = hasRemembered
					&& remembered.heightDelta > 0
					&& remembered.rowVersion >= ManagedRowVersion
					&& remembered.removedBecauseDisabled == false
					&& remembered.heightAfterInsert > 0
					&& runtime.currentHeight == remembered.heightAfterInsert;
				if (shrinkHeight)
				{
					newHeight = Math.Max(runtime.minHeight, runtime.currentHeight - remembered.heightDelta);
					UpdateHeightConfig(configDocument.Root, runtime.modeKey, newHeight);
				}

				var newLayout = runtime.layoutFromXml.Invoke(null, new object[] { new XElement(layoutDocument.Root) });
				if (newLayout == null)
					return;

				state.layouts[runtime.modeKey] = new LayoutState
				{
					path = runtime.layoutPath,
					baselineHash = baselineHash,
					rowVersion = ManagedRowVersion,
					heightDelta = remembered?.heightDelta ?? 0,
					heightAfterInsert = remembered?.heightAfterInsert ?? 0,
					heightRestoredBecauseDisabled = shrinkHeight,
					removedBecauseDisabled = true
				};

				var writes = new Dictionary<string, string>
				{
					{ runtime.layoutPath, ToXml(layoutDocument) },
					{ StateFilePath, SerializeState(state) }
				};
				if (shrinkHeight)
					writes.Add(runtime.configPath, ToXml(configDocument));

				object oldLayout = null;
				object oldHeight = null;
				try
				{
					oldLayout = runtime.layoutProperty.GetValue(null, null);
					oldHeight = runtime.heightValueProperty.GetValue(runtime.heightSetting, null);

					WriteTextTransaction(writes, () =>
					{
						if (shrinkHeight)
							SetHeightValue(runtime, newHeight);
						runtime.layoutProperty.SetValue(null, newLayout, null);
					});
				}
				catch
				{
					TryRestoreRuntime(runtime, oldLayout, oldHeight);
				}
			}
			catch
			{
			}
		}

		static bool ContainsContaminationNeed(XElement root)
		{
			return FindContaminationNeeds(root).Any();
		}

		static IEnumerable<XElement> FindContaminationNeeds(XElement root)
		{
			return root.Descendants().Where(IsContaminationNeed);
		}

		static bool IsContaminationNeed(XElement element)
		{
			return element.Name.LocalName == "Need"
				&& string.Equals((string)element.Attribute("DefName"), ContaminationNeedDefName, StringComparison.Ordinal);
		}

		static bool IsRow(XElement element)
		{
			return element.Name.LocalName == "Row";
		}

		static void RemoveContaminationNeeds(XElement root)
		{
			foreach (var need in FindContaminationNeeds(root).ToList())
			{
				var parent = need.Parent;
				need.Remove();
				if (parent != null && IsRow(parent) && IsEmptyLayoutElement(parent))
					parent.Remove();
			}
		}

		static void UpdateHeightConfig(XElement root, string modeKey, int newHeight)
		{
			var sectionName = modeKey == "Docked" ? "InspectPane" : "Floating";
			var section = root.Element(sectionName);
			if (section == null)
			{
				section = new XElement(sectionName);
				root.Add(section);
			}

			var height = section.Element("Height");
			if (height == null)
				section.Add(new XElement("Height", newHeight.ToString(CultureInfo.InvariantCulture)));
			else
				height.Value = newHeight.ToString(CultureInfo.InvariantCulture);
		}

		static string LayoutHashWithoutContamination(XElement root)
		{
			var clone = new XElement(root);
			RemoveContaminationNeeds(clone);

			var canonical = new StringBuilder();
			AppendCanonical(clone, canonical);
			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
			return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
		}

		static bool IsEmptyLayoutElement(XElement element)
		{
			if (element.Nodes().OfType<XText>().Any(text => string.IsNullOrWhiteSpace(text.Value) == false))
				return false;
			return element.Elements().Any() == false;
		}

		static void AppendCanonical(XElement element, StringBuilder builder)
		{
			builder.Append('<').Append(element.Name.LocalName);
			foreach (var attribute in element.Attributes().OrderBy(attribute => attribute.Name.NamespaceName).ThenBy(attribute => attribute.Name.LocalName))
				builder.Append(' ').Append(attribute.Name.LocalName).Append('=').Append(attribute.Value);
			builder.Append('>');

			foreach (var node in element.Nodes())
			{
				if (node is XElement child)
					AppendCanonical(child, builder);
				else if (node is XText text && string.IsNullOrWhiteSpace(text.Value) == false)
					builder.Append(text.Value.Trim());
			}

			builder.Append("</").Append(element.Name.LocalName).Append('>');
		}

		static void SetHeightValue(RimHudRuntime runtime, int value)
		{
			var targetType = runtime.heightValueProperty.PropertyType;
			var converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
			runtime.heightValueProperty.SetValue(runtime.heightSetting, converted, null);
		}

		static void TryRestoreRuntime(RimHudRuntime runtime, object oldLayout, object oldHeight)
		{
			try
			{
				if (oldHeight != null)
					runtime.heightValueProperty.SetValue(runtime.heightSetting, oldHeight, null);
				if (oldLayout != null)
					runtime.layoutProperty.SetValue(null, oldLayout, null);
			}
			catch
			{
			}
		}

		static void TryApplyRuntimePatches()
		{
			if (rimHudPatchesApplied)
				return;
			try
			{
				var layoutElementType = AccessTools.TypeByName("RimHUD.Interface.Hud.Layout.LayoutElement");
				var labelAndIdGetter = AccessTools.PropertyGetter(layoutElementType, "LabelAndId");
				var postfix = AccessTools.Method(typeof(RimHudIntegration), nameof(LayoutElement_LabelAndId_Postfix));
				if (labelAndIdGetter == null || postfix == null)
					return;

				new Harmony("net.pardeike.zombieland.rimhud").Patch(labelAndIdGetter, postfix: new HarmonyMethod(postfix));
				rimHudPatchesApplied = true;
			}
			catch
			{
			}
		}

		static void LayoutElement_LabelAndId_Postfix(object __instance, ref string __result)
		{
			try
			{
				if (IsContaminationLayoutElement(__instance) == false)
					return;
				__result = $"{RimHudTooltipLabel} [Zombieland]";
			}
			catch
			{
			}
		}

		static bool IsContaminationLayoutElement(object layoutElement)
		{
			var def = AccessTools.Property(layoutElement?.GetType(), "Def")?.GetValue(layoutElement, null) as Def;
			return def != null && string.Equals(def.defName, ContaminationNeedDefName, StringComparison.Ordinal);
		}

		static string ToXml(XDocument document)
		{
			using var writer = new Utf8StringWriter();
			document.Save(writer);
			return writer.ToString();
		}

		static string SerializeState(IntegrationState state)
		{
			return JsonConvert.SerializeObject(state, Formatting.Indented);
		}

		static void WriteTextTransaction(Dictionary<string, string> writes, Action afterWrite)
		{
			var originals = new Dictionary<string, string>();
			var existed = new HashSet<string>();
			var temps = new List<string>();

			try
			{
				foreach (var write in writes)
				{
					var directory = Path.GetDirectoryName(write.Key);
					if (string.IsNullOrEmpty(directory) == false)
						Directory.CreateDirectory(directory);
					if (File.Exists(write.Key))
					{
						existed.Add(write.Key);
						originals[write.Key] = File.ReadAllText(write.Key);
					}

					var temp = write.Key + ".zombieland.tmp";
					File.WriteAllText(temp, write.Value, Encoding.UTF8);
					temps.Add(temp);
				}

				foreach (var write in writes)
					File.Copy(write.Key + ".zombieland.tmp", write.Key, true);

				afterWrite?.Invoke();
			}
			catch
			{
				foreach (var write in writes)
				{
					try
					{
						if (existed.Contains(write.Key))
							File.WriteAllText(write.Key, originals[write.Key], Encoding.UTF8);
						else if (File.Exists(write.Key))
							File.Delete(write.Key);
					}
					catch
					{
					}
				}
				throw;
			}
			finally
			{
				foreach (var temp in temps)
				{
					try
					{
						if (File.Exists(temp))
							File.Delete(temp);
					}
					catch
					{
					}
				}
			}
		}

		sealed class Utf8StringWriter : StringWriter
		{
			public override Encoding Encoding => Encoding.UTF8;
		}
	}
}
