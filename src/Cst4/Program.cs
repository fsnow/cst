using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CST
{
	static class Program
	{
		private static readonly log4net.ILog log = 
			log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			log4net.Config.XmlConfigurator.Configure();

			try
			{
				SplashScreen.ShowSplashScreen();
				SplashScreen.SetStatus(" ");
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new FormMain());
			}
			catch (Exception ex)
			{
				MessageBox.Show("CST4 has crashed. A log file with details of the program error has been " +
					"created in the program's directory named " +
					"\"log.txt\". The developers of the program would appreciate if you would email this " +
					"file to the " +
					"contact email address found on the Contact page of the CST 4.0 website. Thank you." +
					"\n\n" + ex.ToString());

				
				log.Fatal(ex.ToString());
			}
		}
	}
}