using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Dialog_OdysseyLayers : Window
	{
		public List<(Def def, TaggedString name)> allOdysseyLayers;
		public override Vector2 InitialSize => new(320, 480);

		private readonly SettingsGroup settings;
		private Vector2 scrollPosition = Vector2.zero;

		public Dialog_OdysseyLayers(SettingsGroup settings)
		{
			this.settings = settings;

			doCloseButton = true;
			absorbInputAroundWindow = true;
			allOdysseyLayers = OdysseyTools.AllLayers
				.Select(def => (def, name: def.LabelCap))
				.OrderBy(item => item.name.ToString())
				.ToList();
		}

		public override void DoWindowContents(Rect inRect)
		{
			inRect.yMax -= 60;

			var header = "OdysseyLayers".SafeTranslate();
			var num = Text.CalcHeight(header, inRect.width);
			Widgets.Label(new Rect(inRect.xMin, inRect.yMin, inRect.width, num), header);
			inRect.yMin += num + 8;

			// Description for the user
			var description = "OdysseyLayersWithoutZombies".SafeTranslate();
			var descriptionHeight = Text.CalcHeight(description, inRect.width);
			Widgets.Label(new Rect(inRect.xMin, inRect.yMin, inRect.width, descriptionHeight), description);
			inRect.yMin += descriptionHeight + 8;

			var outerRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
			var innerRect = new Rect(0f, 0f, inRect.width - 24f, allOdysseyLayers.Count * (2 + Text.LineHeight));
			Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);

			var list = new Listing_Standard();
			list.Begin(innerRect);
			foreach (var (def, name) in allOdysseyLayers)
			{
				var defName = def.defName;
				var on = settings.allowedOdysseyLayers.Contains(defName);
				var wasOn = on;
				list.CheckboxLabeled(name, ref on, def.description); // Using def.description for tooltip
				if (on && wasOn == false)
					settings.allowedOdysseyLayers.Add(defName);
				if (on == false && wasOn)
					settings.allowedOdysseyLayers.Remove(defName);
			}
			list.End();

			Widgets.EndScrollView();
		}
	}
}
