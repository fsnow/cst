using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
	/// <summary>
	/// A simplification of Windows OS versions. We will use 
	/// </summary>
	public enum WindowsVersion
	{
		Unknown,
		PreXP,
		XP, // and Windows Server 2003 and anything between them and Vista
		Vista, // and Windows Server 2008
		PostVista
	}
}
