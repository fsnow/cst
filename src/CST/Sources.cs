using System;
using System.Collections;
using System.Collections.Generic;

public class Sources
{
	private static Sources sources;

	public static Sources Inst
	{
		get
		{
			if (sources == null)
				sources = new Sources();

			return sources;
		}
	}

	// filename -> SourceType -> Source
	private Dictionary<string, Dictionary<SourceType, Source>> sourcesMap;

	private Sources()
	{
		sourcesMap = new Dictionary<string, Dictionary<SourceType, Source>>();

		addSource("s0101m.mul.xml", SourceType.Burmese1957, 19,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Si%cc%84lakkhandhavaggapa%cc%84l%cc%a3i.pdf");
		addSource("s0102m.mul.xml", SourceType.Burmese1957, 10,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Maha%cc%84vaggapa%cc%84l%cc%a3i.pdf");
		addSource("s0103m.mul.xml", SourceType.Burmese1957, 10,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Pa%cc%84thikavaggapa%cc%84l%cc%a3i.pdf");
		addSource("s0201m.mul.xml", SourceType.Burmese1957, 16,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Mu%cc%84lapan%cc%a3n%cc%a3a%cc%84sapa%cc%84l%cc%a3i.pdf");
		addSource("s0202m.mul.xml", SourceType.Burmese1957, 7,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Majjhimapan%cc%a3n%cc%a3a%cc%84sapa%cc%84l%cc%a3i.pdf");
		addSource("s0203m.mul.xml", SourceType.Burmese1957, 7,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Uparipan%cc%a3n%cc%a3a%cc%84sapa%cc%84l%cc%a3i.pdf");
		addSource("s0301m.mul.xml", SourceType.Burmese1957, 38,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Saga%cc%84tha%cc%84vagga-Nida%cc%84navaggasam%cc%a3yuttapa%cc%84l%cc%a3i.pdf");
		addSource("s0302m.mul.xml", SourceType.Burmese1957, 282,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Saga%cc%84tha%cc%84vagga-Nida%cc%84navaggasam%cc%a3yuttapa%cc%84l%cc%a3i.pdf");
		addSource("s0303m.mul.xml", SourceType.Burmese1957, 19,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Khandhavagga-Sal%cc%a3a%cc%84yatanavaggasam%cc%a3yuttapa%cc%84l%cc%a3i.pdf");
		addSource("s0304m.mul.xml", SourceType.Burmese1957, 254,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Khandhavagga-Sal%cc%a3a%cc%84yatanavaggasam%cc%a3yuttapa%cc%84l%cc%a3i.pdf");
		addSource("s0305m.mul.xml", SourceType.Burmese1957, 19,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Maha%cc%84vaggasam%cc%a3yuttapa%cc%84l%cc%a3i.pdf");
	}

	private void addSource(string filename, SourceType sourceType, int pageStart, string url)
    {
		Dictionary< SourceType, Source > d = new Dictionary<SourceType, Source>();
		d[sourceType] = new Source(sourceType, pageStart, url);
		sourcesMap[filename] = d;
	}
	
	public Source GetSource(string filename, SourceType sourceType)
    {
		var d = sourcesMap[filename];
		if (d == null)
			return null;
		else
			return d[sourceType];
    }

	public enum SourceType
	{
		Burmese1957,
		Burmese2010,
		VriPrint
	}

	public class Source
	{
		public Source(SourceType sourceType, int pageStart, string url)
        {
			SourceType = sourceType;
			PageStart = pageStart;
			Url = url;
        }
		public string Url { get; set; }
		public SourceType SourceType { get; set; }
		public int PageStart { get; set; }
	}
}
