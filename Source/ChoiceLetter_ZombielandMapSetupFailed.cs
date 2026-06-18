using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ChoiceLetter_ZombielandMapSetupFailed : StandardLetter
	{
		public override IEnumerable<DiaOption> Choices
		{
			get
			{
				yield return Option_Close;
				if (lookTargets.IsValid())
					yield return Option_JumpToLocation;
				yield return new DiaOption("ZombielandRevealPlayerLog".Translate())
				{
					action = PlayerLogRevealer.RevealPlayerLog,
					resolveTree = true
				};
			}
		}
	}

	static class PlayerLogRevealer
	{
		public static void RevealPlayerLog()
		{
			var path = Application.consoleLogPath;
			if (path.NullOrEmpty())
			{
				Messages.Message("ZombielandPlayerLogPathUnavailable".Translate(), MessageTypeDefOf.RejectInput, false);
				return;
			}

			try
			{
				if (File.Exists(path))
				{
					RevealPath(path);
					return;
				}
				var directory = Path.GetDirectoryName(path);
				if (directory.NullOrEmpty() == false && Directory.Exists(directory))
				{
					OpenDirectory(directory);
					CopyPath(path);
					Messages.Message("ZombielandPlayerLogRevealFailed".Translate(), MessageTypeDefOf.RejectInput, false);
					return;
				}
			}
			catch (Exception ex)
			{
				Log.Warning($"Zombieland could not reveal Player.log at '{path}': {ex}");
			}

			CopyPath(path);
			Messages.Message("ZombielandPlayerLogRevealFailed".Translate(), MessageTypeDefOf.RejectInput, false);
		}

		static void RevealPath(string path)
		{
			switch (Application.platform)
			{
				case RuntimePlatform.OSXEditor:
				case RuntimePlatform.OSXPlayer:
					StartProcess("/usr/bin/open", $"-R \"{EscapeArgument(path)}\"");
					break;
				case RuntimePlatform.WindowsEditor:
				case RuntimePlatform.WindowsPlayer:
					StartProcess("explorer.exe", $"/select,\"{path}\"");
					break;
				default:
					OpenDirectory(Path.GetDirectoryName(path));
					break;
			}
		}

		static void OpenDirectory(string directory)
		{
			if (directory.NullOrEmpty())
				return;
			switch (Application.platform)
			{
				case RuntimePlatform.OSXEditor:
				case RuntimePlatform.OSXPlayer:
					StartProcess("/usr/bin/open", $"\"{EscapeArgument(directory)}\"");
					break;
				case RuntimePlatform.WindowsEditor:
				case RuntimePlatform.WindowsPlayer:
					StartProcess("explorer.exe", $"\"{directory}\"");
					break;
				default:
					StartProcess("xdg-open", $"\"{EscapeArgument(directory)}\"");
					break;
			}
		}

		static void StartProcess(string fileName, string arguments)
		{
			using var _ = Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				UseShellExecute = false
			});
		}

		static string EscapeArgument(string path)
			=> path?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

		static void CopyPath(string path)
		{
			if (path.NullOrEmpty() == false)
				GUIUtility.systemCopyBuffer = path;
		}
	}
}
