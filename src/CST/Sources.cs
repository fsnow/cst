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
		
		addSource("s0401m.mul.xml", SourceType.Burmese1957, 32,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Ekaka-Duka-Tika-Catukkanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0402m1.mul.xml", SourceType.Burmese1957, 80,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Ekaka-Duka-Tika-Catukkanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0402m2.mul.xml", SourceType.Burmese1957, 130,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Ekaka-Duka-Tika-Catukkanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0402m3.mul.xml", SourceType.Burmese1957, 337,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Ekaka-Duka-Tika-Catukkanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0403m1.mul.xml", SourceType.Burmese1957, 19,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Pan%cc%83caka-Chakka-Sattakanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0403m2.mul.xml", SourceType.Burmese1957, 265,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Pan%cc%83caka-Chakka-Sattakanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0403m3.mul.xml", SourceType.Burmese1957, 412,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Pan%cc%83caka-Chakka-Sattakanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0404m1.mul.xml", SourceType.Burmese1957, 23,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/At%cc%a3t%cc%a3haka-Navaka-Dasaka-Eka%cc%84dasakanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0404m2.mul.xml", SourceType.Burmese1957, 185,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/At%cc%a3t%cc%a3haka-Navaka-Dasaka-Eka%cc%84dasakanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0404m3.mul.xml", SourceType.Burmese1957, 279,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/At%cc%a3t%cc%a3haka-Navaka-Dasaka-Eka%cc%84dasakanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0404m4.mul.xml", SourceType.Burmese1957, 536,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/At%cc%a3t%cc%a3haka-Navaka-Dasaka-Eka%cc%84dasakanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		
		addSource("s0501m.mul.xml", SourceType.Burmese1957, 25,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Khuddakapa%cc%84t%cc%a3ha-Dhammapada-Uda%cc%84na-Itivuttaka-Suttanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0502m.mul.xml", SourceType.Burmese1957, 36,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Khuddakapa%cc%84t%cc%a3ha-Dhammapada-Uda%cc%84na-Itivuttaka-Suttanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0503m.mul.xml", SourceType.Burmese1957, 100,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Khuddakapa%cc%84t%cc%a3ha-Dhammapada-Uda%cc%84na-Itivuttaka-Suttanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0504m.mul.xml", SourceType.Burmese1957, 217,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Khuddakapa%cc%84t%cc%a3ha-Dhammapada-Uda%cc%84na-Itivuttaka-Suttanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0505m.mul.xml", SourceType.Burmese1957, 316,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Khuddakapa%cc%84t%cc%a3ha-Dhammapada-Uda%cc%84na-Itivuttaka-Suttanipa%cc%84tapa%cc%84l%cc%a3i.pdf");
		addSource("s0506m.mul.xml", SourceType.Burmese1957, 17,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Vima%cc%84navatthu-Petavatthu-Theraga%cc%84tha%cc%84-Theri%cc%84ga%cc%84tha%cc%84pa%cc%84l%cc%a3i.pdf");
		addSource("s0507m.mul.xml", SourceType.Burmese1957, 142,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Vima%cc%84navatthu-Petavatthu-Theraga%cc%84tha%cc%84-Theri%cc%84ga%cc%84tha%cc%84pa%cc%84l%cc%a3i.pdf");
		addSource("s0508m.mul.xml", SourceType.Burmese1957, 234,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Vima%cc%84navatthu-Petavatthu-Theraga%cc%84tha%cc%84-Theri%cc%84ga%cc%84tha%cc%84pa%cc%84l%cc%a3i.pdf");
		addSource("s0509m.mul.xml", SourceType.Burmese1957, 391,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Vima%cc%84navatthu-Petavatthu-Theraga%cc%84tha%cc%84-Theri%cc%84ga%cc%84tha%cc%84pa%cc%84l%cc%a3i.pdf");
		addSource("s0510m1.mul.xml", SourceType.Burmese1957, 20,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Apada%cc%84napa%cc%84l%cc%a3i%e2%80%931.pdf");
		addSource("s0510m2.mul.xml", SourceType.Burmese1957, 16,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Apada%cc%84napa%cc%84l%cc%a3i%e2%80%932-%20Buddhavam%cc%a3sa-%20Cariya%cc%84pit%cc%a3akapa%cc%84l%cc%a3i.pdf");
		addSource("s0511m.mul.xml", SourceType.Burmese1957, 311,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Apada%cc%84napa%cc%84l%cc%a3i%e2%80%932-%20Buddhavam%cc%a3sa-%20Cariya%cc%84pit%cc%a3akapa%cc%84l%cc%a3i.pdf");
		addSource("s0512m.mul.xml", SourceType.Burmese1957, 397,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Apada%cc%84napa%cc%84l%cc%a3i%e2%80%932-%20Buddhavam%cc%a3sa-%20Cariya%cc%84pit%cc%a3akapa%cc%84l%cc%a3i.pdf");
		addSource("s0513m.mul.xml", SourceType.Burmese1957, 27,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Ja%cc%84takapa%cc%84l%cc%a3i-1.pdf");
		addSource("s0514m.mul.xml", SourceType.Burmese1957, 5,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Ja%cc%84takapa%cc%84l%cc%a3i-2.pdf");
		addSource("s0515m.mul.xml", SourceType.Burmese1957, 5,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Maha%cc%84niddesapa%cc%84l%cc%a3i.pdf");
		addSource("s0516m.mul.xml", SourceType.Burmese1957, 6,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Cu%cc%84l%cc%a3aniddesapa%cc%84l%cc%a3i.pdf");
		addSource("s0517m.mul.xml", SourceType.Burmese1957, 10,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Pat%cc%a3isambhida%cc%84maggapa%cc%84l%cc%a3i.pdf");
		addSource("s0519m.mul.xml", SourceType.Burmese1957, 7,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Netti-Pet%cc%a3akopadesapa%cc%84l%cc%a3i.pdf");
		addSource("s0518m.nrf.xml", SourceType.Burmese1957, 15,
			"https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Milindapan%cc%83hapa%cc%84l%cc%a3i.pdf");

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
