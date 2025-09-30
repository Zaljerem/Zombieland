using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Dialog_AdvancedSettings : Window
	{
		private static float GetCurX(Listing_Standard list)
		{
			var field = typeof(Listing).GetField("curX", BindingFlags.NonPublic | BindingFlags.Instance);
			return (float)field.GetValue(list);
		}

		private static void SetCurX(Listing_Standard list, float value)
		{
			var field = typeof(Listing).GetField("curX", BindingFlags.NonPublic | BindingFlags.Instance);
			field.SetValue(list, value);
		}

		private static float GetCurY(Listing_Standard list)
		{
			var field = typeof(Listing).GetField("curY", BindingFlags.NonPublic | BindingFlags.Instance);
			return (float)field.GetValue(list);
		}

		private static void SetCurY(Listing_Standard list, float value)
		{
			var field = typeof(Listing).GetField("curY", BindingFlags.NonPublic | BindingFlags.Instance);
			field.SetValue(list, value);
		}

		public override Vector2 InitialSize => new(720, 640);
		private Vector2 scrollPosition = Vector2.zero;

		public Dialog_AdvancedSettings()
		{
			doCloseX = true;
			doCloseButton = true;
			absorbInputAroundWindow = true;
			onlyOneOfTypeAllowed = true;
			closeOnAccept = true;
			closeOnCancel = true;
		}

		public override void PreClose()
		{
			var dict = Constants.Current();
			Constants.Save(dict);
		}

		        float TotalHeight()
				{
					var allSettings = Constants.AllSettings;
					var h = 0f;
					var defaultFontHeight = Text.CalcHeight("test", 100);
					foreach (var (name, field, attr) in allSettings)
					{
						var type = field.FieldType;
						if (type == typeof(bool))
							h += defaultFontHeight;
						else if (type == typeof(int))
							h += 24f;
						else if (type == typeof(float))
							h += 24f;
						else if (type == typeof(int[]))
						{
							var array = (int[])field.GetValue(null);
							h += 24f * array.Length;
						}
						else if (type == typeof(float[]))
						{
							var array = (float[])field.GetValue(null);
							h += 24f * array.Length;
						}
						h += 12; // gap
					}
		
		                        // Add height for Odyssey settings button
		                        if (OdysseyTools.IsActive && OdysseyTools.AllLayers.Any())
		                        {
		                            h += 12; // GapLine
		                            h += 24f; // Button height
		                        }		
					h += 30f; // reset button
					return h;
				}
		public static string TextEntryLabeled(Rect rect, string label, string text, bool labelRight)
		{
			Rect rect2 = rect.LeftHalf().Rounded();
			Rect rect3 = rect.RightHalf().Rounded();
			TextAnchor anchor = Text.Anchor;
			Text.Anchor = labelRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
			Widgets.Label(rect2, label);
			Text.Anchor = anchor;
			if (rect.height <= 30f)
				return Widgets.TextField(rect3, text);
			return Widgets.TextArea(rect3, text, false);
		}

		public void NumericField(Listing_Standard list, string label, ref int value, string tooltip = null, bool labelRight = false)
		{
			var rect = list.GetRect(24f);
			if (tooltip != null)
			{
				if (Mouse.IsOver(rect))
					Widgets.DrawHighlight(rect);
				TooltipHandler.TipRegion(rect, tooltip);
			}
			var str = value.ToString();
			str = TextEntryLabeled(rect, label, str, labelRight);
			if (int.TryParse(str, out var result))
				value = result;
		}

		public void NumericField(Listing_Standard list, string label, ref float value, string tooltip = null, bool labelRight = false)
		{
			var rect = list.GetRect(24f);
			if (tooltip != null)
			{
				if (Mouse.IsOver(rect))
					Widgets.DrawHighlight(rect);
				TooltipHandler.TipRegion(rect, tooltip);
			}
			var str = value.ToString();
			str = TextEntryLabeled(rect, label, str, labelRight);
			if (float.TryParse(str, out var result))
				value = result;
		}

		        public override void DoWindowContents(Rect inRect)
				{
					inRect.yMax -= 60;
		
					var header = "AdvancedSettings".SafeTranslate();
					var num = Text.CalcHeight(header, inRect.width);
					Widgets.Label(new Rect(inRect.xMin, inRect.yMin, inRect.width, num), header);
					inRect.yMin += num + 8;
		
					var allSettings = Constants.AllSettings;
					var list = new Listing_Standard();
		
					var outerRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
					var innerRect = new Rect(0f, 0f, inRect.width - 24f, TotalHeight());
					Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);
		
					list.Begin(innerRect);
					foreach (var (name, field, attr) in allSettings)
					{
						var value = field.GetValue(null);
						var type = field.FieldType;
						if (type == typeof(bool))
						{
							var boolValue = (bool)value;
							list.CheckboxLabeled(name, ref boolValue, attr.Description);
							list.Gap(-list.verticalSpacing);
							field.SetValue(null, boolValue);
						}
						if (type == typeof(int))
						{
							var intValue = (int)value;
							NumericField(list, name, ref intValue, attr.Description);
							field.SetValue(null, intValue);
						}
						if (type == typeof(float))
						{
							var floatValue = (float)value;
							NumericField(list, name, ref floatValue, attr.Description);
							field.SetValue(null, floatValue);
						}
						if (type == typeof(int[]))
						{
							var intArray = (int[])value;
							var savedY = GetCurY(list);
							SetCurY(list, savedY);
							for (var i = 0; i < intArray.Length; i++)
								NumericField(list, $"{i + 1}:", ref intArray[i], null, true);
							field.SetValue(null, intArray);
						}
						if (type == typeof(float[]))
						{
							var floatArray = (float[])value;
							var rect = new Rect(GetCurX(list), GetCurY(list), list.ColumnWidth, 24f * floatArray.Length);
							if (Mouse.IsOver(rect))
								Widgets.DrawHighlight(rect);
							TooltipHandler.TipRegion(rect, attr.Description);
							var savedY = GetCurY(list);
							SetCurY(list, savedY);
							for (var i = 0; i < floatArray.Length; i++)
								NumericField(list, $"{i + 1}:", ref floatArray[i], null, true);
							field.SetValue(null, floatArray);
						}
						list.Gap();
					}
		
		                        // --- Odyssey Settings Button ---
		                        if (OdysseyTools.IsActive && OdysseyTools.AllLayers.Any())
		                        {
		                            list.GapLine();
		                            if (list.ButtonText("OdysseyLayers".SafeTranslate()))
		                            {
		                                Find.WindowStack.Add(new Dialog_OdysseyLayers(ZombieSettings.Values));
		                            }
		                        }
		                        // --- End of Odyssey Settings Button ---		
					if (list.ButtonText("Reset".SafeTranslate()))
					{
						_ = Constants.Apply(Constants.defaultValues);
						scrollPosition = Vector2.zero;
					}
		
					list.End();
		
					Widgets.EndScrollView();
				}
			}
		}