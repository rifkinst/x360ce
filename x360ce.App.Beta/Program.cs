﻿using System;
using System.Linq;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace x360ce.App
{
	static class Program
	{

		public static bool IsDebug
		{
			get
			{
				bool debug;
#if DEBUG
				debug = true;
#endif
				return debug;
			}
		}

		internal const int STATE_VISIBLE = 0x00000002;
		internal const int STATE_ENABLED = 0x00000004;

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
			//var fi = new FileInfo(Application.ExecutablePath);
			//Directory.SetCurrentDirectory(fi.Directory.FullName);

			// IMPORTANT: Make sure this class don't have any static references to x360ce.Engine library or
			// program tries to load x360ce.Engine.dll before AssemblyResolve event is available and fails.
			AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
			if (IsDebug)
			{
				StartApp(args);
				return;
			}
			try
			{
				StartApp(args);
			}
			catch (Exception ex)
			{
				var message = AppHelper.ExceptionToText(ex);
				var box = new Controls.MessageBoxForm();
				if (message.Contains("Could not load file or assembly 'Microsoft.DirectX"))
				{
					message += "===============================================================\r\n";
					message += "You can click the link below to download Microsoft DirectX.";
					box.MainLinkLabel.Text = "http://www.microsoft.com/en-us/download/details.aspx?id=35";
					box.MainLinkLabel.Visible = true;
				}
				var result = box.ShowForm(message, "Exception!", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
				if (result == DialogResult.Cancel)
					Application.Exit();
			}
		}

		public const string InstallVirtualDriverParam = "InstallVirtualDriver";
		public const string UnInstallVirtualDriverParam = "UnInstallVirtualDriver";

		static void StartApp(string[] args)
		{
			//var fi = new FileInfo(Application.ExecutablePath);
			//Directory.SetCurrentDirectory(fi.Directory.FullName);
			// IMPORTANT: Make sure this method don't have any static references to x360ce.Engine library or
			// program tries to load x360ce.Engine.dll before AssemblyResolve event is available and fails.
			AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
			if (!RuntimePolicyHelper.LegacyV2RuntimeEnabledSuccessfully)
			{
				// Failed to enable useLegacyV2RuntimeActivationPolicy at runtime.
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			// Requires System.Configuration.Installl reference.
			var ic = new System.Configuration.Install.InstallContext(null, args);
			if (ic.Parameters.ContainsKey(InstallVirtualDriverParam))
			{
				DInput.VirtualDriverInstaller.InstallVirtualDriver();
				return;
			}
			if (ic.Parameters.ContainsKey(UnInstallVirtualDriverParam))
			{
				DInput.VirtualDriverInstaller.UnInstallVirtualDriver();
				return;
			}
			if (ic.Parameters.ContainsKey("Settings"))
			{
				OpenSettingsFolder(Application.UserAppDataPath);
				OpenSettingsFolder(Application.CommonAppDataPath);
				OpenSettingsFolder(Application.LocalUserAppDataPath);
				return;
			}
			if (!CheckSettings()) return;
			MainForm.Current = new MainForm();
			if (ic.Parameters.ContainsKey("Exit"))
			{
				MainForm.Current.BroadcastMessage(MainForm.wParam_Close);
				return;
			}
			var doNotAllowToRun = SettingsManager.Options.AllowOnlyOneCopy && MainForm.Current.BroadcastMessage(MainForm.wParam_Restore);
			// If one copy is already oppened then...
			if (doNotAllowToRun)
			{
				// Dispose properly so that the tray icon will be removed.
				MainForm.Current.Dispose();
			}
			else
			{
				Application.Run(MainForm.Current);
			}
		}

		public static bool IsClosing;

		public static object DeviceLock = new object();

		public static int TimerCount = 0;
		public static int ReloadCount = 0;
		public static int ErrorCount = 0;

		public static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
		{
			if (IsClosing) return;
			ErrorCount++;
			MainForm.Current.UpdateTimer.Stop();
			MainForm.Current.UpdateStatus("- " + e.Exception.Message);
			MainForm.Current.UpdateTimer.Start();
		}

		static void OpenSettingsFolder(string path)
		{
			var di = new DirectoryInfo(path);
			//if (!di.Exists) return;
			//if (di.GetFiles().Length == 0) return;
			var psi = new ProcessStartInfo(di.Parent.Parent.FullName);
			psi.UseShellExecute = true;
			psi.ErrorDialog = true;
			Process.Start(psi);
		}

		static bool CheckSettings()
		{
			try
			{
				Properties.Settings.Default.Reload();
			}
			catch (ConfigurationErrorsException ex)
			{
				// Requires System.Configuration
				string filename = ((ConfigurationErrorsException)ex.InnerException).Filename;
				var title = "Corrupt user settings of " + Application.ProductName;
				var text =
					"Program has detected that your user settings file has become corrupted. " +
					"This may be due to a crash or improper exiting of the program. " +
					"Program must reset your user settings in order to continue.\r\n" +
					"Click [Yes] to reset your user settings and continue.\r\n" +
					"Click [No] if you wish to exit and attempt manual repair.";
				var result = MessageBox.Show(text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Error);
				if (result == DialogResult.Yes)
				{
					File.Delete(filename);
					Properties.Settings.Default.Reload();
				}
				else
				{
					OpenSettingsFolder(Application.UserAppDataPath);
					OpenSettingsFolder(Application.CommonAppDataPath);
					OpenSettingsFolder(Application.LocalUserAppDataPath);
					return false;
				}
			}
			return true;
		}


		static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs e)
		{
			string dllName = e.Name.Contains(",") ? e.Name.Substring(0, e.Name.IndexOf(',')) : e.Name.Replace(".dll", "");
			Stream sr = null;
			switch (dllName)
			{
				case "vXboxInterface":
				case "x360ce.Engine":
				case "x360ce.Engine.XmlSerializers":
				case "SharpDX":
				case "SharpDX.DirectInput":
				case "SharpDX.RawInput":
					sr = GetResourceStream(dllName + ".dll");
					break;
				default:
					break;
			}
			if (sr == null)
				return null;
			byte[] bytes = new byte[sr.Length];
			sr.Read(bytes, 0, bytes.Length);
			var asm = Assembly.Load(bytes);
			return asm;
		}

		/// <summary>
		/// Get 32-bit or 64-bit resource depending on x360ce.exe platform.
		/// </summary>
		public static Stream GetResourceStream(string name)
		{
			var path = GetResourcePath(name);
			if (path == null)
				return null;
			var assembly = Assembly.GetEntryAssembly();
			var sr = assembly.GetManifestResourceStream(path);
			return sr;
		}

		/// <summary>
		/// Get 32-bit or 64-bit resource depending on x360ce.exe platform.
		/// </summary>
		public static string GetResourcePath(string name)
		{
			var assembly = Assembly.GetEntryAssembly();
			var names = assembly.GetManifestResourceNames()
				.Where(x => x.EndsWith(name));
			var architecture = assembly.GetName().ProcessorArchitecture;
			var a = architecture == ProcessorArchitecture.Amd64 ? ".x64." : ".x86.";
			// Try to get by architecture first.
			var path = names.FirstOrDefault(x => x.Contains(a));
			if (!string.IsNullOrEmpty(path))
				return path;
			// Return first found.
			return names.FirstOrDefault();
		}

	}
}
