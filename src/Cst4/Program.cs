using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CST
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			SplashScreen.ShowSplashScreen();
			SplashScreen.SetStatus(" ");
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new FormMain());
		}
	}
}