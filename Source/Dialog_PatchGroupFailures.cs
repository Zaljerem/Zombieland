using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Dialog_PatchGroupFailures : Window
	{
		const float RowHeight = 28f;
		readonly List<PatchGroupResult> rows;
		Vector2 scrollPosition;

		public override Vector2 InitialSize => new(420f, 320f);

		public Dialog_PatchGroupFailures(IEnumerable<PatchGroupResult> results)
		{
			rows = results
				.Where(result => result.State != PatchGroupState.Skipped)
				.OrderBy(result => result.Order)
				.ToList();

			forcePause = true;
			absorbInputAroundWindow = true;
			onlyOneOfTypeAllowed = true;
			closeOnAccept = true;
			closeOnCancel = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			Text.Font = GameFont.Medium;
			Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Zombieland had trouble");
			inRect.yMin += 38f;

			Text.Font = GameFont.Small;
			Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 48f), "Some features were turned off:");
			inRect.yMin += 42f;

			var buttonRect = new Rect(inRect.xMax - 120f, inRect.yMax - 38f, 120f, 32f);
			var scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 48f);
			var viewRect = new Rect(0f, 0f, scrollRect.width - 16f, rows.Count * RowHeight);

			Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect, true);
			for (var i = 0; i < rows.Count; i++)
				DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), rows[i]);
			Widgets.EndScrollView();

			if (Widgets.ButtonText(buttonRect, "Done"))
				Close();
		}

		static void DrawRow(Rect rect, PatchGroupResult result)
		{
			if (Mouse.IsOver(rect))
				Widgets.DrawHighlight(rect);

			var stateRect = new Rect(rect.xMax - 32f, rect.y + 2f, 28f, rect.height - 4f);
			var labelRect = rect;
			labelRect.xMax = stateRect.xMin - 6f;
			labelRect.xMin += 4f;

			Text.Anchor = TextAnchor.MiddleLeft;
			GUI.color = Color.white;
			Widgets.Label(labelRect, result.Label);

			Text.Anchor = TextAnchor.MiddleCenter;
			GUI.color = result.IsFailure ? new Color(1f, 0.25f, 0.25f) : new Color(0.35f, 0.9f, 0.45f);
			Widgets.Label(stateRect, result.IsFailure ? "✕" : "✓");
			GUI.color = Color.white;
			Text.Anchor = TextAnchor.UpperLeft;

			if (result.IsFailure && string.IsNullOrEmpty(result.Summary) == false)
				TooltipHandler.TipRegion(rect, result.Summary);
		}
	}
}
