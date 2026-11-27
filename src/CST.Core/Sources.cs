using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CST.Navigation;

namespace CST;

public class Sources
{
	// Thread-safe lazy init: an unlocked "if (sources == null) sources = new Sources()" could
	// construct two instances under a startup/indexing race. (CORE-1)
	private static readonly Lazy<Sources> instance = new(() => new Sources());

	public static Sources Inst => instance.Value;

	// filename -> SourceType -> Source
	private Dictionary<string, Dictionary<SourceType, Source>> sourcesMap;

	// ===========================================
	// SharePoint path constants
	// ===========================================
	// Base paths within the _Source folder (SharePointService adds the _Source prefix)

	private const string Burmese1957Base = "01 - Burmese-CST/1957 edition";
	private const string Burmese2010Base = "01 - Burmese-CST/2010 edition";

	// 1957 edition subdirectories
	private const string MulaVinaya1957 = "1 - Mula - Vinaya";
	private const string MulaSutta1957 = "2 - Mula - Sutta";
	private const string MulaAbhidhamma1957 = "3 - Mula - Abhidhamma";
	private const string Atthakatha1957 = "4 - Atthakatha";
	private const string Tika1957 = "5 - Tika_";

	// 2010 edition subdirectory
	private const string Mula2010 = "Mula";

	// Anya (other texts) base path and subdirectories
	private const string BurmeseAnyaBase = "01 - Burmese-CST/Anya";
	private const string Anya_Visuddhimagga = "1. Visuddhimagga";

	// Combined base paths
	private static string Burmese1957Vinaya => $"{Burmese1957Base}/{MulaVinaya1957}";
	private static string Burmese1957Sutta => $"{Burmese1957Base}/{MulaSutta1957}";
	private static string Burmese1957Abhidhamma => $"{Burmese1957Base}/{MulaAbhidhamma1957}";
	private static string Burmese1957Atthakatha => $"{Burmese1957Base}/{Atthakatha1957}";
	private static string Burmese1957Tika => $"{Burmese1957Base}/{Tika1957}";
	private static string Burmese2010Mula => $"{Burmese2010Base}/{Mula2010}";
	private static string BurmeseAnyaVisuddhimagga => $"{BurmeseAnyaBase}/{Anya_Visuddhimagga}";
	// Anya groups 2–9 (folder names copied byte-for-byte from SharePoint). (#418)
	private static string BurmeseAnyaSangayana => $"{BurmeseAnyaBase}/2. Saṅgāyana-puccha vissajjanā";
	private static string BurmeseAnyaLedi => $"{BurmeseAnyaBase}/3. Leḍī sayāḍo gantha-saṅgaho";
	private static string BurmeseAnyaBuddhaVandana => $"{BurmeseAnyaBase}/4. Buddha-vandanā gantha-saṅgaho";
	private static string BurmeseAnyaVamsa => $"{BurmeseAnyaBase}/5. Vaṃsa-gantha-saṅgaho";
	private static string BurmeseAnyaByakarana => $"{BurmeseAnyaBase}/6. Byākaraṇa gantha-saṅgaho";
	private static string BurmeseAnyaNiti => $"{BurmeseAnyaBase}/7. Nīti-gantha-saṅgaho";
	private static string BurmeseAnyaPakinnaka => $"{BurmeseAnyaBase}/8. Pakiṇṇaka-gantha-saṅgaho";
	private static string BurmeseAnyaSihala => $"{BurmeseAnyaBase}/9. Sihaḷa-gantha-saṅgaho";

	// ===========================================
	// 1957 Edition PDF filenames
	// ===========================================

	// Vinaya
	private const string Vin_Parajika_1957 = "Pārājikapāḷi.pdf";
	private const string Vin_Pacittiya_1957 = "Pācittiyapāḷi.pdf";
	private const string Vin_Mahavagga_1957 = "Mahāvaggapāḷi.pdf";
	private const string Vin_Culavagga_1957 = "Cūḷavaggapāḷi.pdf";
	private const string Vin_Parivara_1957 = "Parivārapāḷi.pdf";

	// Sutta - DN
	private const string DN_Silakkhandhavagga_1957 = "Sīlakkhandhavaggapāḷi.pdf";
	private const string DN_Mahavagga_1957 = "Mahāvaggapāḷi.pdf";
	private const string DN_Pathikavagga_1957 = "Pāthikavaggapāḷi.pdf";

	// Sutta - MN
	private const string MN_Mulapannasa_1957 = "Mūlapaṇṇāsapāḷi.pdf";
	private const string MN_Majjhimapannasa_1957 = "Majjhimapaṇṇāsapāḷi.pdf";
	private const string MN_Uparipannasa_1957 = "Uparipaṇṇāsapāḷi.pdf";

	// Sutta - SN
	private const string SN_SagathaNidana_1957 = "Sagāthāvagga-Nidānavaggasaṃyuttapāḷi.pdf";
	private const string SN_KhandhaSalayatana_1957 = "Khandhavagga-Saḷāyatanavaggasaṃyuttapāḷi.pdf";
	private const string SN_Mahavagga_1957 = "Mahāvaggasaṃyuttapāḷi.pdf";

	// Sutta - AN
	private const string AN_EkakaCatukka_1957 = "Ekaka-Duka-Tika-Catukkanipātapāḷi.pdf";
	private const string AN_PancakaSattaka_1957 = "Pañcaka-Chakka-Sattakanipātapāḷi.pdf";
	private const string AN_AtthakaEkadasaka_1957 = "Aṭṭhaka-Navaka-Dasaka-Ekādasakanipātapāḷi.pdf";

	// Sutta - KN
	private const string KN_KhuddakaSuttanipata_1957 = "Khuddakapāṭha-Dhammapada-Udāna-Itivuttaka-Suttanipātapāḷi.pdf";
	private static readonly int[] KN_KhuddakaSuttanipata_1957_Missing = { 276, 277, 278 };
	private static readonly int[] KN_KhuddakaSuttanipata_1957_Extra = Enumerable.Repeat(219, 99).ToArray();
	private const string KN_VimanavattuTherigatha_1957 = "Vimānavatthu-Petavatthu-Theragāthā-Therīgāthāpāḷi.pdf";
	private const string KN_Apadana1_1957 = "Apadānapāḷi–1.pdf";
	private const string KN_Apadana2Buddhavamsa_1957 = "Apadānapāḷi–2- Buddhavaṃsa- Cariyāpiṭakapāḷi.pdf";
	private const string KN_Jataka1_1957 = "Jātakapāḷi-1.pdf";
	private const string KN_Jataka2_1957 = "Jātakapāḷi-2.pdf";
	private const string KN_Mahaniddesa_1957 = "Mahāniddesapāḷi.pdf";
	private const string KN_Culaniddesa_1957 = "Cūḷaniddesapāḷi.pdf";
	private const string KN_Patisambhidamagga_1957 = "Paṭisambhidāmaggapāḷi.pdf";
	private const string KN_NettiPetakopadesa_1957 = "Netti-Peṭakopadesapāḷi.pdf";
	private const string KN_Milindapanha_1957 = "Milindapañhapāḷi.pdf";

	// Abhidhamma
	private const string Abh_Dhammasangani_1957 = "Dhammasaṅgaṇīpāḷi.pdf";
	private const string Abh_Vibhanga_1957 = "Vibhaṅgapāḷi.pdf";
	private const string Abh_DhatukathaPuggalapannatti_1957 = "Dhātukathā-Puggalapaññattipāḷi.pdf";
	private const string Abh_Kathavatthu_1957 = "Kathāvatthupāḷi.pdf";
	private const string Abh_Yamaka1_1957 = "Yamakapāḷi-1.pdf";
	private const string Abh_Yamaka2_1957 = "Yamakapāḷi-2.pdf";
	private const string Abh_Yamaka3_1957 = "Yamakapāḷi-3.pdf";
	private const string Abh_Patthana1_1957 = "Paṭṭhānapāḷi-1.pdf";
	private const string Abh_Patthana2_1957 = "Paṭṭhānapāḷi-2.pdf";
	private const string Abh_Patthana3_1957 = "Paṭṭhānapāḷi-3.pdf";
	private const string Abh_Patthana4_1957 = "Paṭṭhānapāḷi-4.pdf";
	private const string Abh_Patthana5_1957 = "Paṭṭhānapāḷi-5.pdf";

	// ===========================================
	// 1957 Atthakatha (Commentary) PDF filenames
	// ===========================================

	// Vinaya Atthakatha
	private const string Att_Vin_Parajika1_1957 = "Pārājikakaṇḍa-aṭṭhakathā-1.pdf";
	private const string Att_Vin_Parajika2_1957 = "Pārājikakaṇḍa-aṭṭhakathā-2.pdf"; // vin01a vol. 2 (M 2.0001-2.0312) starts on PDF page 6 (verified by hand, 2026-11-09). Unmapped until #546: the multi-volume book currently opens vol. 1
	private const string Att_Vin_Pacittiyadi_1957 = "Pācityādi-aṭṭhakathā.pdf";
	private const string Att_Vin_Culavagga_1957 = "Cūḷavaggādi-aṭṭhakathā.pdf";

	// DN Atthakatha
	private const string Att_DN_Silakkhandhavagga_1957 = "Sīlakkhandhavagga-aṭṭhakathā.pdf";
	private const string Att_DN_Mahavagga_1957 = "Mahāvagga-aṭṭhakathā.pdf";
	private const string Att_DN_Pathikavagga_1957 = "Pāthikavagga-aṭṭhakathā.pdf";

	// MN Atthakatha
	private const string Att_MN_Mulapannasa1_1957 = "Mūlapaṇṇāsa-aṭṭhakathā -1.pdf";
	private const string Att_MN_Mulapannasa2_1957 = "Mūlapaṇṇāsa-aṭṭhakathā -2.pdf"; // TODO(#76): vol-2 mapping pending upstream pagination redesign; the multi-volume book currently opens vol.1
	private const string Att_MN_Majjhimapannasa_1957 = "Majjhimapaṇṇāsa-aṭṭhakathā.pdf";
	private const string Att_MN_Uparipannasa_1957 = "Uparipaṇṇāsa-aṭṭhakathā.pdf";

	// SN Atthakatha
	private const string Att_SN_Sagathavagga_1957 = "Sagāthāvagga-aṭṭhakathā.pdf";
	private const string Att_SN_NidanaKhandha_1957 = "Nidānavagga-Khandhavagga-aṭṭhakathā.pdf";
	private const string Att_SN_SalayatanaMaha_1957 = "Saḷāyatanavagga-Mahāvagga-aṭṭhakathā.pdf";

	// AN Atthakatha
	private const string Att_AN_Ekakanipata_1957 = "Ekakanipāta-aṭṭhakathā.pdf";
	private const string Att_AN_DukaTikaCatukka_1957 = "Duka-tika-catukkanipāta-aṭṭhakathā.pdf";
	private const string Att_AN_Pancakanipata_1957 = "Pañcakanipātādi-aṭṭhakathā.pdf";

	// KN Atthakatha
	private const string Att_KN_Khuddakapatha_1957 = "Khuddakapāṭha-aṭṭhakathā.pdf";
	private const string Att_KN_Dhammapada1_1957 = "Dhammapada-aṭṭhakathā-1.pdf";
	private const string Att_KN_Dhammapada2_1957 = "Dhammapada-aṭṭhakathā-2.pdf"; // TODO(#76): vol-2 mapping pending upstream pagination redesign; the multi-volume book currently opens vol.1
	private const string Att_KN_Udana_1957 = "Udāna-aṭṭhakathā.pdf";
	private const string Att_KN_Itivuttaka_1957 = "Itivuttaka-aṭṭhakathā.pdf";
	private const string Att_KN_Suttanipata1_1957 = "Suttanipāta-aṭṭhakathā - 1.pdf";
	private const string Att_KN_Suttanipata2_1957 = "Suttanipāta-aṭṭhakathā - 2.pdf"; // TODO(#76): vol-2 mapping pending upstream pagination redesign; the multi-volume book currently opens vol.1
	private const string Att_KN_Vimanavatthu_1957 = "Vimānavatthu-aṭṭhakathā.pdf";
	private const string Att_KN_Petavatthu_1957 = "Petavatthu-aṭṭhakathā.pdf";
	private const string Att_KN_Theragatha1_1957 = "Theragāthā-aṭṭhakathā-1.pdf";
	private const string Att_KN_Theragatha2_1957 = "Theragāthā-aṭṭhakathā-2.pdf";
	private const string Att_KN_Therigatha_1957 = "Therīgāthā-aṭṭhakathā.pdf";
	private const string Att_KN_Apadana1_1957 = "Apadāna-aṭṭhakathā-1.pdf";
	private const string Att_KN_Apadana2_1957 = "Apadāna-aṭṭhakathā-2.pdf"; // TODO(#76): vol-2 mapping pending upstream pagination redesign; the multi-volume book currently opens vol.1
	private const string Att_KN_Buddhavamsa_1957 = "Buddhavaṃsa-aṭṭhakathā.pdf";
	private const string Att_KN_Cariyapitaka_1957 = "Cariyāpiṭaka-aṭṭhakathā.pdf";
	private const string Att_KN_Jataka1_1957 = "Jātaka-aṭṭhakathā-1.pdf";
	private const string Att_KN_Jataka2_1957 = "Jātaka-aṭṭhakathā-2.pdf";
	private const string Att_KN_Jataka3_1957 = "Jātaka-aṭṭhakathā-3.pdf";
	private const string Att_KN_Jataka4_1957 = "Jātaka-aṭṭhakathā-4.pdf";
	private const string Att_KN_Jataka5_1957 = "Jātaka-aṭṭhakathā-5.pdf";
	private const string Att_KN_Jataka6_1957 = "Jātaka-aṭṭhakathā-6.pdf";
	private const string Att_KN_Jataka7_1957 = "Jātaka-aṭṭhakathā-7.pdf";
	private const string Att_KN_Mahaniddesa_1957 = "Mahāniddesa-aṭṭhakathā.pdf";
	private const string Att_KN_CulaniddesaNetti_1957 = "Cūḷaniddesa-aṭṭhakathā Netti-aṭṭhakathā.pdf";
	private const string Att_KN_Patisambhida1_1957 = "Paṭisambhidāmagga-aṭṭhakathā-1.pdf";
	private const string Att_KN_Patisambhida2_1957 = "Paṭisambhidāmagga-aṭṭhakathā-2.pdf"; // TODO(#76): vol-2 mapping pending upstream pagination redesign; the multi-volume book currently opens vol.1

	// Abhidhamma Atthakatha
	private const string Att_Abh_Dhammasangani_1957 = "Dhammasaṅgaṇī-aṭṭhakathā.pdf";
	private const string Att_Abh_Vibhanga_1957 = "Vibhaṅga-aṭṭhakathā.pdf";
	private const string Att_Abh_Pancappakarana_1957 = "Pañcappakaraṇa-aṭṭhakathā.pdf";

	// ===========================================
	// 1957 Tika (Sub-Commentary) PDF filenames
	// ===========================================

	// DN Tika
	private const string Tik_DN_Silakkhandhavagga_1957 = "Sīlakkhandhavagga-ṭīkā.pdf";
	private const string Tik_DN_Mahavagga_1957 = "Mahāvagga-ṭīkā.pdf";
	private const string Tik_DN_Pathikavagga_1957 = "Pāthikavagga-ṭīkā.pdf";

	// MN Tika
	private const string Tik_MN_Mulapannasa1_1957 = "Mūlapaṇṇāsa-ṭīkā-1.pdf";
	private const string Tik_MN_Mulapannasa2_1957 = "Mūlapaṇṇāsa-ṭīkā-2.pdf";
	private const string Tik_MN_MajjhimaUpari_1957 = "Majjhimapaṇṇāsa-Uparipaṇṇāsa-ṭīkā.pdf";

	// SN Tika
	private const string Tik_SN_Samyutta1_1957 = "Saṃyuttaṭīkā -1.pdf";
	private const string Tik_SN_Samyutta2_1957 = "Saṃyuttaṭīkā -2.pdf";

	// AN Tika
	private const string Tik_AN_Ekakanipata_1957 = "Ekakanipāta-ṭīkā.pdf";
	private const string Tik_AN_DukaTikaCatukka_1957 = "Duka-tika-catukkanipāta-ṭīkā.pdf";
	private const string Tik_AN_Pancakanipata_1957 = "Pañcakanipātādi-ṭīkā.pdf";

	// KN Tika
	private const string Tik_KN_Netti_1957 = "Nettiṭīkā - Nettivibhāvinī.pdf";

	// Vinaya Tika (Saratthadipani)
	private const string Tik_Vin_Saratthadipani1_1957 = "Sāratthadīpanī-ṭīkā-1.pdf";
	private const string Tik_Vin_Saratthadipani2_1957 = "Sāratthadīpanī-ṭīkā-2.pdf";
	private const string Tik_Vin_Saratthadipani3_1957 = "Sāratthadīpanī-ṭīkā-3.pdf";

	// Abhidhamma Tika (Mulatika + Anutika combined)
	private const string Tik_Abh_Dhammasangani_1957 = "Dhammasaṅgaṇī-mūlaṭīkā-anuṭīkā.pdf";
	private const string Tik_Abh_Vibhanga_1957 = "Vibhaṅga-mūlaṭīkā-anuṭīkā.pdf";
	private const string Tik_Abh_Pancappakarana_1957 = "Pañcappakaraṇa-mūlaṭīkā-anuṭīkā.pdf";

	// Ṭīkā folder: the Sīlakkhandhavagga-abhinavaṭīkā, the Vinaya ṭīkās and the later Abhidhamma texts
	private const string Tik_DN_SilakkhandhaAbhinava1_1957 = "Sīlakkhandhavagga-abhinavaṭīkā-1.pdf";
	private const string Tik_DN_SilakkhandhaAbhinava2_1957 = "Sīlakkhandhavagga-abhinavaṭīkā-2.pdf";
	private const string Tik_Vin_Dvematika_1957 = "Dvematika_Bhikkhupatimokkha_Bhikkhunipatimokkhapatha.pdf";
	private const string Tik_Vin_Vinayasangaha_1957 = "Vinayasaṅgahaṭṭhakathā.pdf";
	private const string Tik_Vin_Vajirabuddhi_1957 = "Vajirabuddhi-ṭīkā.pdf";
	private const string Tik_Vin_Vimativinodani1_1957 = "Vimativinodanī-ṭīkā-1.pdf";
	private const string Tik_Vin_Vimativinodani2_1957 = "Vimativinodanī-ṭīkā-2.pdf"; // vin07t vol. 2, unmapped until #546
	private const string Tik_Vin_Vinayalankara1_1957 = "Vinayalankara Tika Part - 1.pdf";
	private const string Tik_Vin_Vinayalankara2_1957 = "Vinayalankara Tika - 2.pdf"; // vin08t vol. 2, unmapped until #546
	private const string Tik_Vin_KankhaPurana_1957 = "Kaṅkhāvitaraṇī purāṇa-abhinava-ṭīkā.pdf";
	private const string Tik_Vin_Vinayavinicchaya_1957 = "Vinayavinicchayo-uttaravinicchayo.pdf";
	private const string Tik_Vin_VinayavinicchayaTika1_1957 = "Vinayavinicchaya Tika Part 1.pdf";
	private const string Tik_Vin_VinayavinicchayaTika2_1957 = "Vinayavinicchaya Tika Part 2.pdf"; // vin11t vol. 2, unmapped until #546. Starts at PDF page 9; print 500-501 are placeholder pages, so no MissingPages.
	private const string Tik_Vin_Pacityadiyojana_1957 = "Pacityadiyojanapali-1-2.pdf";
	private const string Tik_Vin_Khuddasikkha_1957 = "Khuddasikkhā-mūlasikkhā.pdf";
	private const string Tik_Abh_Abhidhammavatara_1957 = "Abhidhammavatara etc..pdf";
	private const string Tik_Abh_AbhidhammatthaSangaha_1957 = "Tika-Abhidhammatthasangaho.pdf";
	private const string Tik_Abh_AbhidhammavataraTika1_1957 = "Abhidhammāvatāra purāṇa-abhinava-ṭīkā-1.pdf";
	private const string Tik_Abh_AbhidhammavataraTika2_1957 = "Abhidhammavatara Abhinava Tika-2.pdf"; // abh08t vol. 2, unmapped until #546
	private const string Tik_Abh_Mohavicchedani_1957 = "Mohavicchedanī (abhidhammamātikā).pdf";

	// ===========================================
	// Anya (Other Texts) PDF filenames
	// ===========================================

	// Visuddhimagga and Mahatika
	private const string Anya_Visuddhimagga1 = "1. Visuddhimagga-1.pdf";
	private const string Anya_Visuddhimagga2 = "2. Visuddhimagga-2.pdf";
	private const string Anya_VisuddhimaggaMahatika1 = "3. Visuddhimagga-Mahāṭīkā-1.pdf";
	private const string Anya_VisuddhimaggaMahatika2 = "4. Visuddhimagga-Mahāṭīkā-2.pdf";
	private const string Anya_VisuddhimaggaNidanakatha = "5. Visuddhimagga-Nidānakathā.pdf";

	// ===========================================
	// 2010 Edition PDF filenames
	// ===========================================

	// Vinaya
	private const string Vin_Parajika_2010 = "001-Pārājikapāḷi.pdf";
	private const string Vin_Pacittiya_2010 = "002-Pācittiyapāḷi.pdf";
	private const string Vin_Mahavagga_2010 = "003-Mahāvaggapāḷi.pdf";
	private const string Vin_Culavagga_2010 = "004-Cūḷavaggapāḷi.pdf";
	private const string Vin_Parivara_2010 = "005-Parivārapāḷi.pdf";

	// Sutta - DN
	private const string DN_Silakkhandhavagga_2010 = "006-Sīlakkhandhavaggapāḷi.pdf";
	private const string DN_Mahavagga_2010 = "007-Mahāvaggapāḷi.pdf";
	private const string DN_Pathikavagga_2010 = "008-Pāthikavaggapāḷi.pdf";

	// Sutta - MN
	private const string MN_Mulapannasa_2010 = "009-Mūlapaṇṇāsapāḷi (only pp1-197).pdf";
	private const string MN_Majjhimapannasa_2010 = "010-Majjhimapaṇṇāsapāḷi.pdf";
	private const string MN_Uparipannasa_2010 = "011-Uparipaṇṇāsapāḷi.pdf";

	// Sutta - SN
	private const string SN_SagathaNidana_2010 = "012-Sagāthāvagga-Nidānavaggasaṃyuttapāḷi.pdf";
	private const string SN_KhandhaSalayatana_2010 = "013-Khandhavagga-Saḷāyatanavaggasaṃyuttapāḷi.pdf";
	private const string SN_Mahavagga_2010 = "014-Mahāvaggasaṃyuttapāḷi.pdf";

	// Sutta - AN
	private const string AN_EkakaCatukka_2010 = "015-Ekaka-Duka-Tika-Catukkanipātapāḷi.pdf";
	private const string AN_PancakaSattaka_2010 = "016-Pañcaka-Chakka-Sattaka-nipātapāḷi.pdf";
	private const string AN_AtthakaEkadasaka_2010 = "017-Aṭṭhaka-Navaka-Dasaka-Ekādasakanipātapāḷi.pdf";

	// Sutta - KN
	private const string KN_Patisambhidamagga_2010 = "018-Paṭisambhidāmaggapāḷi.pdf";
	private const string KN_KhuddakaSuttanipata_2010 = "019-Khuddakapāṭha-Dhammapada-Udāna-Itivuttaka-Suttanipātapāḷi.pdf";
	private const string KN_VimanavattuTherigatha_2010 = "020-Vimānavatthu-PetavatthuTheragāthā-Therīgāthāpāḷi.pdf";
	private const string KN_Apadana1_2010 = "021-Apadānapāḷi - 1.pdf";
	private const string KN_Apadana2Buddhavamsa_2010 = "022-Apadānapāḷi – 2 Buddhavaṃsa- Cariyāpiṭakapāḷi (2010).pdf"; // SharePoint also holds a (2005) printing
	private const string KN_Jataka1_2010 = "023-Jātakapāḷi-1.pdf";
	private const string KN_Jataka2_2010 = "024-Jātakapāḷi-2.pdf";
	private const string KN_Mahaniddesa_2010 = "025-Mahāniddesapāḷi.pdf";
	private const string KN_Culaniddesa_2010 = "026-Cūḷaniddesapāḷi.pdf";
	private const string KN_NettiPetakopadesa_2010 = "027-Netti-Peṭakopadesapāḷi.pdf";
	private const string KN_Milindapanha_2010 = "028-Milindapañhapāḷi.pdf";

	// Abhidhamma
	private const string Abh_Dhammasangani_2010 = "029-Dhammasaṅgaṇīpāḷi.pdf";
	private const string Abh_Vibhanga_2010 = "030-Vibhaṅgapāḷi.pdf";
	private const string Abh_DhatukathaPuggalapannatti_2010 = "031-Dhātukathā-Puggalapaññattipāḷi.pdf";
	private const string Abh_Kathavatthu_2010 = "032-Kathāvatthupāḷi.pdf";
	private const string Abh_Yamaka1_2010 = "033-Yamakapāḷi-1.pdf";
	private const string Abh_Yamaka2_2010 = "034-Yamakapāḷi-2.pdf";
	private const string Abh_Yamaka3_2010 = "035-Yamakapāḷi-3.pdf";
	private const string Abh_Patthana1_2010 = "036-Paṭṭhānapāḷi-1.pdf";
	private const string Abh_Patthana2_2010 = "037-Paṭṭhānapāḷi-2.pdf";
	private const string Abh_Patthana3_2010 = "038-Paṭṭhānapāḷi-3.pdf";
	private const string Abh_Patthana4_2010 = "039-Paṭṭhānapāḷi-4.pdf";
	private const string Abh_Patthana5_2010 = "040-Paṭṭhānapāḷi-5.pdf";

	private Sources()
	{
		sourcesMap = new Dictionary<string, Dictionary<SourceType, Source>>();

		// =============================================
		// 1957 Edition Mappings
		// =============================================

		// --- Vinaya Pitaka ---
		addSource("vin01m.mul.xml", SourceType.Burmese1957, 23, $"{Burmese1957Vinaya}/{Vin_Parajika_1957}");
		addSource("vin02m1.mul.xml", SourceType.Burmese1957, 15, $"{Burmese1957Vinaya}/{Vin_Pacittiya_1957}");
		addSource("vin02m2.mul.xml", SourceType.Burmese1957, 15, $"{Burmese1957Vinaya}/{Vin_Mahavagga_1957}");
		addSource("vin02m3.mul.xml", SourceType.Burmese1957, 11, $"{Burmese1957Vinaya}/{Vin_Culavagga_1957}", new[] { 282, 283 });
		addSource("vin02m4.mul.xml", SourceType.Burmese1957, 15, $"{Burmese1957Vinaya}/{Vin_Parivara_1957}");

		// --- Sutta Pitaka - Dīgha Nikāya ---
		addSource("s0101m.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Sutta}/{DN_Silakkhandhavagga_1957}");
		addSource("s0102m.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Sutta}/{DN_Mahavagga_1957}");
		addSource("s0103m.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Sutta}/{DN_Pathikavagga_1957}");

		// --- Sutta Pitaka - Majjhima Nikāya ---
		addSource("s0201m.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{MN_Mulapannasa_1957}");
		addSource("s0202m.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Sutta}/{MN_Majjhimapannasa_1957}");
		addSource("s0203m.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Sutta}/{MN_Uparipannasa_1957}");

		// --- Sutta Pitaka - Saṃyutta Nikāya ---
		addSource("s0301m.mul.xml", SourceType.Burmese1957, 38, $"{Burmese1957Sutta}/{SN_SagathaNidana_1957}", extraPagesAfter: new[] { 155, 155 });
		addSource("s0302m.mul.xml", SourceType.Burmese1957, 40, $"{Burmese1957Sutta}/{SN_SagathaNidana_1957}");
		addSource("s0303m.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Sutta}/{SN_KhandhaSalayatana_1957}");
		addSource("s0304m.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Sutta}/{SN_KhandhaSalayatana_1957}");
		addSource("s0305m.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Sutta}/{SN_Mahavagga_1957}");

		// --- Sutta Pitaka - Aṅguttara Nikāya ---
		addSource("s0401m.mul.xml", SourceType.Burmese1957, 32, $"{Burmese1957Sutta}/{AN_EkakaCatukka_1957}");
		addSource("s0402m1.mul.xml", SourceType.Burmese1957, 32, $"{Burmese1957Sutta}/{AN_EkakaCatukka_1957}");
		addSource("s0402m2.mul.xml", SourceType.Burmese1957, 32, $"{Burmese1957Sutta}/{AN_EkakaCatukka_1957}");
		addSource("s0402m3.mul.xml", SourceType.Burmese1957, 31, $"{Burmese1957Sutta}/{AN_EkakaCatukka_1957}");
		addSource("s0403m1.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Sutta}/{AN_PancakaSattaka_1957}");
		addSource("s0403m2.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Sutta}/{AN_PancakaSattaka_1957}");
		addSource("s0403m3.mul.xml", SourceType.Burmese1957, 18, $"{Burmese1957Sutta}/{AN_PancakaSattaka_1957}");
		addSource("s0404m1.mul.xml", SourceType.Burmese1957, 23, $"{Burmese1957Sutta}/{AN_AtthakaEkadasaka_1957}");
		addSource("s0404m2.mul.xml", SourceType.Burmese1957, 23, $"{Burmese1957Sutta}/{AN_AtthakaEkadasaka_1957}");
		addSource("s0404m3.mul.xml", SourceType.Burmese1957, 23, $"{Burmese1957Sutta}/{AN_AtthakaEkadasaka_1957}");
		addSource("s0404m4.mul.xml", SourceType.Burmese1957, 22, $"{Burmese1957Sutta}/{AN_AtthakaEkadasaka_1957}");

		// --- Sutta Pitaka - Khuddaka Nikāya ---
		addSource("s0501m.mul.xml", SourceType.Burmese1957, 25, $"{Burmese1957Sutta}/{KN_KhuddakaSuttanipata_1957}");
		addSource("s0502m.mul.xml", SourceType.Burmese1957, 24, $"{Burmese1957Sutta}/{KN_KhuddakaSuttanipata_1957}");
		addSource("s0503m.mul.xml", SourceType.Burmese1957, 24, $"{Burmese1957Sutta}/{KN_KhuddakaSuttanipata_1957}");
		// Checked by hand in the scan (2026-11-13):
		//  - after print 219 (PDF 241) the scan starts over at print 120 (PDF 242) and reaches print 220
		//    at PDF 341: 99 extra pages;
		//  - print 275 is labelled "279" in the scan; it holds the start of ¶112, as the XML's M 275 does;
		//  - print 276-277, the end of the Itivuttaka, are not in the scan, nor is the blank 278; the
		//    next PDF page is the first page of the Suttanipāta.
		addSource("s0504m.mul.xml", SourceType.Burmese1957, 23, $"{Burmese1957Sutta}/{KN_KhuddakaSuttanipata_1957}",
			KN_KhuddakaSuttanipata_1957_Missing, extraPagesAfter: KN_KhuddakaSuttanipata_1957_Extra);
		addSource("s0505m.mul.xml", SourceType.Burmese1957, 23, $"{Burmese1957Sutta}/{KN_KhuddakaSuttanipata_1957}",
			KN_KhuddakaSuttanipata_1957_Missing, extraPagesAfter: KN_KhuddakaSuttanipata_1957_Extra);
		addSource("s0506m.mul.xml", SourceType.Burmese1957, 17, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0507m.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0508m.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0509m.mul.xml", SourceType.Burmese1957, 15, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0510m1.mul.xml", SourceType.Burmese1957, 20, $"{Burmese1957Sutta}/{KN_Apadana1_1957}");
		addSource("s0510m2.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{KN_Apadana2Buddhavamsa_1957}", new[] { 186, 214, 215 });
		addSource("s0511m.mul.xml", SourceType.Burmese1957, 13, $"{Burmese1957Sutta}/{KN_Apadana2Buddhavamsa_1957}");
		addSource("s0512m.mul.xml", SourceType.Burmese1957, 13, $"{Burmese1957Sutta}/{KN_Apadana2Buddhavamsa_1957}");
		addSource("s0513m.mul.xml", SourceType.Burmese1957, 27, $"{Burmese1957Sutta}/{KN_Jataka1_1957}", new[] { 80, 81, 264, 265, 270, 271 });
		addSource("s0514m.mul.xml", SourceType.Burmese1957, 5, $"{Burmese1957Sutta}/{KN_Jataka2_1957}");
		addSource("s0515m.mul.xml", SourceType.Burmese1957, 5, $"{Burmese1957Sutta}/{KN_Mahaniddesa_1957}", extraPagesAfter: new[] { 215, 215 });
		addSource("s0516m.mul.xml", SourceType.Burmese1957, 6, $"{Burmese1957Sutta}/{KN_Culaniddesa_1957}");
		addSource("s0517m.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Sutta}/{KN_Patisambhidamagga_1957}");
		addSource("s0519m.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Sutta}/{KN_NettiPetakopadesa_1957}", new[] { 156, 157 });
		addSource("s0518m.nrf.xml", SourceType.Burmese1957, 15, $"{Burmese1957Sutta}/{KN_Milindapanha_1957}");
		addSource("s0520m.nrf.xml", SourceType.Burmese1957, 5, $"{Burmese1957Sutta}/{KN_NettiPetakopadesa_1957}");

		// --- Abhidhamma Pitaka ---
		addSource("abh01m.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Abhidhamma}/{Abh_Dhammasangani_1957}");
		addSource("abh02m.mul.xml", SourceType.Burmese1957, 11, $"{Burmese1957Abhidhamma}/{Abh_Vibhanga_1957}");
		// abh03m1 = Dhatukatha, abh03m2 = Puggalapannatti (both in combined PDF)
		addSource("abh03m1.mul.xml", SourceType.Burmese1957, 8, $"{Burmese1957Abhidhamma}/{Abh_DhatukathaPuggalapannatti_1957}");
		addSource("abh03m2.mul.xml", SourceType.Burmese1957, 8, $"{Burmese1957Abhidhamma}/{Abh_DhatukathaPuggalapannatti_1957}");
		addSource("abh03m3.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Abhidhamma}/{Abh_Kathavatthu_1957}");
		addSource("abh03m4.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Abhidhamma}/{Abh_Yamaka1_1957}", new[] { 16 }, extraPagesAfter: new[] { 19, 19 });
		addSource("abh03m5.mul.xml", SourceType.Burmese1957, 12, $"{Burmese1957Abhidhamma}/{Abh_Yamaka2_1957}");
		addSource("abh03m6.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Abhidhamma}/{Abh_Yamaka3_1957}");
		addSource("abh03m7.mul.xml", SourceType.Burmese1957, 30, $"{Burmese1957Abhidhamma}/{Abh_Patthana1_1957}");
		addSource("abh03m8.mul.xml", SourceType.Burmese1957, 28, $"{Burmese1957Abhidhamma}/{Abh_Patthana2_1957}", extraPagesAfter: new[] { 95, 95 }); // scan repeats print pages 94-95
		addSource("abh03m9.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Abhidhamma}/{Abh_Patthana3_1957}", new[] { 506, 507 });
		addSource("abh03m10.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Abhidhamma}/{Abh_Patthana4_1957}", new[] { 280, 281, 466 });
		addSource("abh03m11.mul.xml", SourceType.Burmese1957, 6, $"{Burmese1957Abhidhamma}/{Abh_Patthana5_1957}", new[] { 62, 122, 158, 196, 214, 306, 326, 348, 376, 402, 424 });

		// =============================================
		// 2010 Edition Mappings
		// =============================================

		// --- Vinaya Pitaka ---
		addSource("vin01m.mul.xml", SourceType.Burmese2010, 25, $"{Burmese2010Mula}/{Vin_Parajika_2010}");
		addSource("vin02m1.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{Vin_Pacittiya_2010}");
		addSource("vin02m2.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{Vin_Mahavagga_2010}");
		addSource("vin02m3.mul.xml", SourceType.Burmese2010, 15, $"{Burmese2010Mula}/{Vin_Culavagga_2010}");
		addSource("vin02m4.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{Vin_Parivara_2010}");

		// --- Sutta Pitaka - Dīgha Nikāya ---
		addSource("s0101m.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{DN_Silakkhandhavagga_2010}");
		addSource("s0102m.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{DN_Mahavagga_2010}");
		addSource("s0103m.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{DN_Pathikavagga_2010}");

		// --- Sutta Pitaka - Majjhima Nikāya ---
		addSource("s0201m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{MN_Mulapannasa_2010}", new[] { 379 });
		addSource("s0202m.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{MN_Majjhimapannasa_2010}");
		addSource("s0203m.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{MN_Uparipannasa_2010}", new[] { 202, 203 });

		// --- Sutta Pitaka - Saṃyutta Nikāya ---
		addSource("s0301m.mul.xml", SourceType.Burmese2010, 43, $"{Burmese2010Mula}/{SN_SagathaNidana_2010}");
		addSource("s0302m.mul.xml", SourceType.Burmese2010, 43, $"{Burmese2010Mula}/{SN_SagathaNidana_2010}");
		addSource("s0303m.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{SN_KhandhaSalayatana_2010}");
		addSource("s0304m.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{SN_KhandhaSalayatana_2010}");
		addSource("s0305m.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{SN_Mahavagga_2010}");

		// --- Sutta Pitaka - Aṅguttara Nikāya ---
		addSource("s0401m.mul.xml", SourceType.Burmese2010, 35, $"{Burmese2010Mula}/{AN_EkakaCatukka_2010}");
		addSource("s0402m1.mul.xml", SourceType.Burmese2010, 35, $"{Burmese2010Mula}/{AN_EkakaCatukka_2010}");
		addSource("s0402m2.mul.xml", SourceType.Burmese2010, 35, $"{Burmese2010Mula}/{AN_EkakaCatukka_2010}");
		addSource("s0402m3.mul.xml", SourceType.Burmese2010, 35, $"{Burmese2010Mula}/{AN_EkakaCatukka_2010}");
		addSource("s0403m1.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{AN_PancakaSattaka_2010}");
		addSource("s0403m2.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{AN_PancakaSattaka_2010}");
		addSource("s0403m3.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{AN_PancakaSattaka_2010}");
		addSource("s0404m1.mul.xml", SourceType.Burmese2010, 27, $"{Burmese2010Mula}/{AN_AtthakaEkadasaka_2010}");
		addSource("s0404m2.mul.xml", SourceType.Burmese2010, 27, $"{Burmese2010Mula}/{AN_AtthakaEkadasaka_2010}");
		addSource("s0404m3.mul.xml", SourceType.Burmese2010, 27, $"{Burmese2010Mula}/{AN_AtthakaEkadasaka_2010}");
		addSource("s0404m4.mul.xml", SourceType.Burmese2010, 27, $"{Burmese2010Mula}/{AN_AtthakaEkadasaka_2010}");

		// --- Sutta Pitaka - Khuddaka Nikāya ---
		addSource("s0501m.mul.xml", SourceType.Burmese2010, 29, $"{Burmese2010Mula}/{KN_KhuddakaSuttanipata_2010}");
		addSource("s0502m.mul.xml", SourceType.Burmese2010, 29, $"{Burmese2010Mula}/{KN_KhuddakaSuttanipata_2010}");
		addSource("s0503m.mul.xml", SourceType.Burmese2010, 29, $"{Burmese2010Mula}/{KN_KhuddakaSuttanipata_2010}");
		addSource("s0504m.mul.xml", SourceType.Burmese2010, 29, $"{Burmese2010Mula}/{KN_KhuddakaSuttanipata_2010}", new[] { 244, 245 });
		addSource("s0505m.mul.xml", SourceType.Burmese2010, 27, $"{Burmese2010Mula}/{KN_KhuddakaSuttanipata_2010}");
		addSource("s0506m.mul.xml", SourceType.Burmese2010, 21, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}", new[] { 120, 121 });
		addSource("s0507m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}");
		addSource("s0508m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}");
		addSource("s0509m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}");
		addSource("s0510m1.mul.xml", SourceType.Burmese2010, 25, $"{Burmese2010Mula}/{KN_Apadana1_2010}");
		addSource("s0510m2.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Apadana2Buddhavamsa_2010}");
		addSource("s0511m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Apadana2Buddhavamsa_2010}");
		addSource("s0512m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Apadana2Buddhavamsa_2010}");
		addSource("s0513m.mul.xml", SourceType.Burmese2010, 31, $"{Burmese2010Mula}/{KN_Jataka1_2010}");
		addSource("s0514m.mul.xml", SourceType.Burmese2010, 9, $"{Burmese2010Mula}/{KN_Jataka2_2010}");
		addSource("s0515m.mul.xml", SourceType.Burmese2010, 9, $"{Burmese2010Mula}/{KN_Mahaniddesa_2010}");
		addSource("s0516m.mul.xml", SourceType.Burmese2010, 9, $"{Burmese2010Mula}/{KN_Culaniddesa_2010}");
		addSource("s0517m.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{KN_Patisambhidamagga_2010}");
		addSource("s0519m.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{KN_NettiPetakopadesa_2010}");
		addSource("s0518m.nrf.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Milindapanha_2010}");
		addSource("s0520m.nrf.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{KN_NettiPetakopadesa_2010}");

		// --- Abhidhamma Pitaka ---
		addSource("abh01m.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{Abh_Dhammasangani_2010}", new[] { 138, 139 });
		addSource("abh02m.mul.xml", SourceType.Burmese2010, 15, $"{Burmese2010Mula}/{Abh_Vibhanga_2010}");
		// abh03m1 = Dhatukatha, abh03m2 = Puggalapannatti (both in combined PDF)
		addSource("abh03m1.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{Abh_DhatukathaPuggalapannatti_2010}");
		addSource("abh03m2.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{Abh_DhatukathaPuggalapannatti_2010}");
		addSource("abh03m3.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{Abh_Kathavatthu_2010}");
		addSource("abh03m4.mul.xml", SourceType.Burmese2010, 15, $"{Burmese2010Mula}/{Abh_Yamaka1_2010}");
		addSource("abh03m5.mul.xml", SourceType.Burmese2010, 15, $"{Burmese2010Mula}/{Abh_Yamaka2_2010}");
		addSource("abh03m6.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{Abh_Yamaka3_2010}");
		addSource("abh03m7.mul.xml", SourceType.Burmese2010, 35, $"{Burmese2010Mula}/{Abh_Patthana1_2010}", new[] { 284, 285 });
		addSource("abh03m8.mul.xml", SourceType.Burmese2010, 31, $"{Burmese2010Mula}/{Abh_Patthana2_2010}", new[] { 376, 377, 378, 379 });
		addSource("abh03m9.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{Abh_Patthana3_2010}");
		addSource("abh03m10.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{Abh_Patthana4_2010}");
		addSource("abh03m11.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{Abh_Patthana5_2010}", extraPagesAfter: new[] { 112, 112 });

		// =============================================
		// 1957 Atthakatha (Commentary) Mappings
		// =============================================
		// Note: Only 1957 has Atthakatha PDFs available

		// --- Vinaya Atthakatha ---
		addSource("vin01a.att.xml", SourceType.Burmese1957, 15, $"{Burmese1957Atthakatha}/{Att_Vin_Parajika1_1957}", extraPagesAfter: new[] { 135, 135 });
		addSource("vin02a1.att.xml", SourceType.Burmese1957, 11, $"{Burmese1957Atthakatha}/{Att_Vin_Pacittiyadi_1957}");
		addSource("vin02a2.att.xml", SourceType.Burmese1957, 10, $"{Burmese1957Atthakatha}/{Att_Vin_Pacittiyadi_1957}"); // the Mahāvagga-aṭṭhakathā continues vin02a1 in the Pācityādi volume: its title page (print 233) is PDF 242
		addSource("vin02a3.att.xml", SourceType.Burmese1957, 12, $"{Burmese1957Atthakatha}/{Att_Vin_Culavagga_1957}");
		addSource("vin02a4.att.xml", SourceType.Burmese1957, 12, $"{Burmese1957Atthakatha}/{Att_Vin_Culavagga_1957}");

		// --- DN Atthakatha ---
		addSource("s0101a.att.xml", SourceType.Burmese1957, 16, $"{Burmese1957Atthakatha}/{Att_DN_Silakkhandhavagga_1957}");
		addSource("s0102a.att.xml", SourceType.Burmese1957, 9, $"{Burmese1957Atthakatha}/{Att_DN_Mahavagga_1957}");
		addSource("s0103a.att.xml", SourceType.Burmese1957, 9, $"{Burmese1957Atthakatha}/{Att_DN_Pathikavagga_1957}");

		// --- MN Atthakatha ---
		addSource("s0201a.att.xml", SourceType.Burmese1957, 16, $"{Burmese1957Atthakatha}/{Att_MN_Mulapannasa1_1957}");
		addSource("s0202a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_MN_Majjhimapannasa_1957}");
		addSource("s0203a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_MN_Uparipannasa_1957}", new[] { 64, 65, 142, 143 });

		// --- SN Atthakatha ---
		addSource("s0301a.att.xml", SourceType.Burmese1957, 24, $"{Burmese1957Atthakatha}/{Att_SN_Sagathavagga_1957}");
		addSource("s0302a.att.xml", SourceType.Burmese1957, 18, $"{Burmese1957Atthakatha}/{Att_SN_NidanaKhandha_1957}");
		addSource("s0303a.att.xml", SourceType.Burmese1957, 17, $"{Burmese1957Atthakatha}/{Att_SN_NidanaKhandha_1957}");
		addSource("s0304a.att.xml", SourceType.Burmese1957, 23, $"{Burmese1957Atthakatha}/{Att_SN_SalayatanaMaha_1957}");
		addSource("s0305a.att.xml", SourceType.Burmese1957, 23, $"{Burmese1957Atthakatha}/{Att_SN_SalayatanaMaha_1957}");

		// --- AN Atthakatha ---
		addSource("s0401a.att.xml", SourceType.Burmese1957, 16, $"{Burmese1957Atthakatha}/{Att_AN_Ekakanipata_1957}");
		addSource("s0402a.att.xml", SourceType.Burmese1957, 19, $"{Burmese1957Atthakatha}/{Att_AN_DukaTikaCatukka_1957}", new[] { 68, 248 });
		addSource("s0403a.att.xml", SourceType.Burmese1957, 27, $"{Burmese1957Atthakatha}/{Att_AN_Pancakanipata_1957}", new[] { 86, 144 });
		addSource("s0404a.att.xml", SourceType.Burmese1957, 27, $"{Burmese1957Atthakatha}/{Att_AN_Pancakanipata_1957}", new[] { 86, 144, 192, 256, 286, 342 });

		// --- KN Atthakatha ---
		addSource("s0501a.att.xml", SourceType.Burmese1957, 17, $"{Burmese1957Atthakatha}/{Att_KN_Khuddakapatha_1957}");
		addSource("s0502a.att.xml", SourceType.Burmese1957, 8, $"{Burmese1957Atthakatha}/{Att_KN_Dhammapada1_1957}");
		addSource("s0503a.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Udana_1957}");
		addSource("s0504a.att.xml", SourceType.Burmese1957, 8, $"{Burmese1957Atthakatha}/{Att_KN_Itivuttaka_1957}");
		addSource("s0505a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Suttanipata1_1957}", new[] { 176, 177 });
		addSource("s0506a.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Vimanavatthu_1957}");
		addSource("s0507a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_KN_Petavatthu_1957}", extraPagesAfter: new[] { 195 });
		addSource("s0508a1.att.xml", SourceType.Burmese1957, 11, $"{Burmese1957Atthakatha}/{Att_KN_Theragatha1_1957}");
		addSource("s0508a2.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Theragatha2_1957}");
		addSource("s0509a.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Therigatha_1957}");
		addSource("s0510a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Apadana1_1957}", new[] { 190, 191 });
		addSource("s0511a.att.xml", SourceType.Burmese1957, 5, $"{Burmese1957Atthakatha}/{Att_KN_Buddhavamsa_1957}");
		addSource("s0512a.att.xml", SourceType.Burmese1957, 5, $"{Burmese1957Atthakatha}/{Att_KN_Cariyapitaka_1957}", new[] { 278, 279, 280, 281, 282, 283, 284, 285 });
		addSource("s0513a1.att.xml", SourceType.Burmese1957, 10, $"{Burmese1957Atthakatha}/{Att_KN_Jataka1_1957}");
		addSource("s0513a2.att.xml", SourceType.Burmese1957, 10, $"{Burmese1957Atthakatha}/{Att_KN_Jataka2_1957}");
		addSource("s0513a3.att.xml", SourceType.Burmese1957, 10, $"{Burmese1957Atthakatha}/{Att_KN_Jataka3_1957}", extraPagesAfter: new[] { 407, 407 });
		addSource("s0513a4.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Jataka4_1957}", extraPagesAfter: new[] { 231, 231 });
		addSource("s0514a1.att.xml", SourceType.Burmese1957, 3, $"{Burmese1957Atthakatha}/{Att_KN_Jataka5_1957}");
		addSource("s0514a2.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Jataka6_1957}", new[] { 38, 118 });
		addSource("s0514a3.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Jataka7_1957}", new[] { 2, 3, 150 });
		addSource("s0515a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Mahaniddesa_1957}", new[] { 366, 367, 368, 369 });
		addSource("s0516a.att.xml", SourceType.Burmese1957, 5, $"{Burmese1957Atthakatha}/{Att_KN_CulaniddesaNetti_1957}");
		addSource("s0517a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_KN_Patisambhida1_1957}");
		addSource("s0519a.att.xml", SourceType.Burmese1957, 181, $"{Burmese1957Atthakatha}/{Att_KN_CulaniddesaNetti_1957}", new[] { 236, 237 });

		// --- Abhidhamma Atthakatha ---
		addSource("abh01a.att.xml", SourceType.Burmese1957, 14, $"{Burmese1957Atthakatha}/{Att_Abh_Dhammasangani_1957}");
		addSource("abh02a.att.xml", SourceType.Burmese1957, 9, $"{Burmese1957Atthakatha}/{Att_Abh_Vibhanga_1957}");
		addSource("abh03a.att.xml", SourceType.Burmese1957, 13, $"{Burmese1957Atthakatha}/{Att_Abh_Pancappakarana_1957}", new[] { 24 });

		// =============================================
		// 1957 Tika (Sub-Commentary) Mappings
		// =============================================

		// --- DN Tika ---
		addSource("s0101t.tik.xml", SourceType.Burmese1957, 18, $"{Burmese1957Tika}/{Tik_DN_Silakkhandhavagga_1957}");
		addSource("s0102t.tik.xml", SourceType.Burmese1957, 10, $"{Burmese1957Tika}/{Tik_DN_Mahavagga_1957}");
		addSource("s0103t.tik.xml", SourceType.Burmese1957, 9, $"{Burmese1957Tika}/{Tik_DN_Pathikavagga_1957}");

		// --- MN Tika ---
		// Note: s0201t spans two PDF volumes (Mūlapaṇṇāsa-ṭīkā-1 and Mūlapaṇṇāsa-ṭīkā-2)
		// TODO: Requires special case - parse volume number from page marker (1.x vs 2.x) to select PDF
		addSource("s0201t.tik.xml", SourceType.Burmese1957, 16, $"{Burmese1957Tika}/{Tik_MN_Mulapannasa1_1957}");
		// Note: s0202t and s0203t are combined in one PDF
		addSource("s0202t.tik.xml", SourceType.Burmese1957, 9, $"{Burmese1957Tika}/{Tik_MN_MajjhimaUpari_1957}");
		addSource("s0203t.tik.xml", SourceType.Burmese1957, 8, $"{Burmese1957Tika}/{Tik_MN_MajjhimaUpari_1957}");

		// --- SN Tika ---
		// Note: 5 XML files map to 2 PDF volumes
		addSource("s0301t.tik.xml", SourceType.Burmese1957, 22, $"{Burmese1957Tika}/{Tik_SN_Samyutta1_1957}");
		addSource("s0302t.tik.xml", SourceType.Burmese1957, 36, $"{Burmese1957Tika}/{Tik_SN_Samyutta2_1957}");
		addSource("s0303t.tik.xml", SourceType.Burmese1957, 36, $"{Burmese1957Tika}/{Tik_SN_Samyutta2_1957}");
		addSource("s0304t.tik.xml", SourceType.Burmese1957, 35, $"{Burmese1957Tika}/{Tik_SN_Samyutta2_1957}");
		addSource("s0305t.tik.xml", SourceType.Burmese1957, 34, $"{Burmese1957Tika}/{Tik_SN_Samyutta2_1957}");

		// --- AN Tika ---
		addSource("s0401t.tik.xml", SourceType.Burmese1957, 16, $"{Burmese1957Tika}/{Tik_AN_Ekakanipata_1957}");
		addSource("s0402t.tik.xml", SourceType.Burmese1957, 17, $"{Burmese1957Tika}/{Tik_AN_DukaTikaCatukka_1957}");
		addSource("s0403t.tik.xml", SourceType.Burmese1957, 19, $"{Burmese1957Tika}/{Tik_AN_Pancakanipata_1957}", new[] { 148 });
		addSource("s0404t.tik.xml", SourceType.Burmese1957, 18, $"{Burmese1957Tika}/{Tik_AN_Pancakanipata_1957}", new[] { 266 });

		// --- KN Tika ---
		addSource("s0519t.tik.xml", SourceType.Burmese1957, 7, $"{Burmese1957Tika}/{Tik_KN_Netti_1957}");
		addSource("s0501t.nrf.xml", SourceType.Burmese1957, 166, $"{Burmese1957Tika}/{Tik_KN_Netti_1957}");

		// --- Vinaya Tika (Saratthadipani) ---
		addSource("vin01t1.tik.xml", SourceType.Burmese1957, 14, $"{Burmese1957Tika}/{Tik_Vin_Saratthadipani1_1957}");
		addSource("vin01t2.tik.xml", SourceType.Burmese1957, 9, $"{Burmese1957Tika}/{Tik_Vin_Saratthadipani2_1957}");
		addSource("vin02t.tik.xml", SourceType.Burmese1957, 21, $"{Burmese1957Tika}/{Tik_Vin_Saratthadipani3_1957}", new[] { 130, 364 });

		// --- Abhidhamma Tika ---
		addSource("abh01t.tik.xml", SourceType.Burmese1957, 16, $"{Burmese1957Tika}/{Tik_Abh_Dhammasangani_1957}");
		addSource("abh02t.tik.xml", SourceType.Burmese1957, 9, $"{Burmese1957Tika}/{Tik_Abh_Vibhanga_1957}");
		addSource("abh03t.tik.xml", SourceType.Burmese1957, 20, $"{Burmese1957Tika}/{Tik_Abh_Pancappakarana_1957}", new[] { 46 });

		// --- Ṭīkā-folder books first mapped 2026-11-10 ---
		// A start page of 1 means "not yet measured", not "starts on page 1"; each is being found by hand.
		// The missing pages are the isolated gaps in each book's Myanmar page markers, seeded as in #540.
		// Not yet handled:
		//  - vin07t, vin08t, vin11t and abh08t restart their numbering in a second volume, so their
		//    volume-2 PDFs (named above) wait for #546. vin07t's and vin11t's volume-2 gaps wait with them.
		//  - abh06t holds four texts, each numbered from 1 (pages 1-186, 1-160, 1-100, 1-34); abh09t
		//    holds the Abhidhammamātikā (1-95) and then the Mohavicchedanī (1-488). Only the first
		//    text of each can be reached until a source can tell those runs apart.
		//  - abh09t's 1957 scan, though, numbers one run, print 1 at PDF 16 through 476 at PDF 491, so
		//    which of the two texts that run belongs to is not yet checked.
		addSource("abh04t.nrf.xml", SourceType.Burmese1957, 239, $"{Burmese1957Tika}/{Tik_Abh_Dhammasangani_1957}");
		addSource("abh05t.nrf.xml", SourceType.Burmese1957, 287, $"{Burmese1957Tika}/{Tik_Abh_Pancappakarana_1957}", new[] { 34, 56, 158 });
		addSource("abh06t.nrf.xml", SourceType.Burmese1957, 1, $"{Burmese1957Tika}/{Tik_Abh_Abhidhammavatara_1957}");
		addSource("abh07t.nrf.xml", SourceType.Burmese1957, 1, $"{Burmese1957Tika}/{Tik_Abh_AbhidhammatthaSangaha_1957}");
		addSource("abh08t.nrf.xml", SourceType.Burmese1957, 10, $"{Burmese1957Tika}/{Tik_Abh_AbhidhammavataraTika1_1957}");
		addSource("abh09t.nrf.xml", SourceType.Burmese1957, 16, $"{Burmese1957Tika}/{Tik_Abh_Mohavicchedani_1957}", new[] { 388, 389 }, extraPagesAfter: new[] { 87, 87 });
		addSource("s0104t.nrf.xml", SourceType.Burmese1957, 5, $"{Burmese1957Tika}/{Tik_DN_SilakkhandhaAbhinava1_1957}");
		addSource("s0105t.nrf.xml", SourceType.Burmese1957, 8, $"{Burmese1957Tika}/{Tik_DN_SilakkhandhaAbhinava2_1957}");
		addSource("vin04t.nrf.xml", SourceType.Burmese1957, 30, $"{Burmese1957Tika}/{Tik_Vin_Dvematika_1957}");
		addSource("vin05t.nrf.xml", SourceType.Burmese1957, 6, $"{Burmese1957Tika}/{Tik_Vin_Vinayasangaha_1957}", new[] { 203, 204, 206, 210, 212 }, extraPagesAfter: new[] { 111, 111, 111, 111, 111, 111, 111, 111, 111, 111, 202, 205, 209, 211 });
		addSource("vin06t.nrf.xml", SourceType.Burmese1957, 23, $"{Burmese1957Tika}/{Tik_Vin_Vajirabuddhi_1957}", new[] { 390, 506 });
		addSource("vin07t.nrf.xml", SourceType.Burmese1957, 9, $"{Burmese1957Tika}/{Tik_Vin_Vimativinodani1_1957}");
		addSource("vin08t.nrf.xml", SourceType.Burmese1957, 1, $"{Burmese1957Tika}/{Tik_Vin_Vinayalankara1_1957}");
		addSource("vin09t.nrf.xml", SourceType.Burmese1957, 19, $"{Burmese1957Tika}/{Tik_Vin_KankhaPurana_1957}", new[] { 118, 416, 417, 450 });
		addSource("vin10t.nrf.xml", SourceType.Burmese1957, 15, $"{Burmese1957Tika}/{Tik_Vin_Vinayavinicchaya_1957}");
		addSource("vin11t.nrf.xml", SourceType.Burmese1957, 15, $"{Burmese1957Tika}/{Tik_Vin_VinayavinicchayaTika1_1957}");
		addSource("vin12t.nrf.xml", SourceType.Burmese1957, 1, $"{Burmese1957Tika}/{Tik_Vin_Pacityadiyojana_1957}", new[] { 408, 546 });
		addSource("vin13t.nrf.xml", SourceType.Burmese1957, 15, $"{Burmese1957Tika}/{Tik_Vin_Khuddasikkha_1957}", new[] { 46, 58, 236, 442 }, extraPagesAfter: new[] { 427, 427 },
			pageOverrides: new() { [47] = 61, [48] = 60 }); // scan binds print 48 before 47

		// =============================================
		// Anya (Other Texts) Mappings
		// =============================================

		// --- Visuddhimagga ---
		// e0101n and e0102n are Mula (root text), e0103n and e0104n are Mahatika (sub-commentary)
		// e0105n is the Nidanakatha (introduction/origin story)
		addSource("e0101n.mul.xml", SourceType.BurmeseAnya, 7, $"{BurmeseAnyaVisuddhimagga}/{Anya_Visuddhimagga1}");
		addSource("e0102n.mul.xml", SourceType.BurmeseAnya, 6, $"{BurmeseAnyaVisuddhimagga}/{Anya_Visuddhimagga2}");
		addSource("e0103n.att.xml", SourceType.BurmeseAnya, 8, $"{BurmeseAnyaVisuddhimagga}/{Anya_VisuddhimaggaMahatika1}");
		addSource("e0104n.att.xml", SourceType.BurmeseAnya, 8, $"{BurmeseAnyaVisuddhimagga}/{Anya_VisuddhimaggaMahatika2}");
		addSource("e0105n.nrf.xml", SourceType.BurmeseAnya, 2, $"{BurmeseAnyaVisuddhimagga}/{Anya_VisuddhimaggaNidanakatha}");

		// --- Anya groups 2–9 (#418) ---
		// PATHS are mapped here. START PAGES come from inspecting each scan — a person opening the PDF and
		// reading where the text actually begins. There is nothing to derive them from. 18 are now real
		// (a reviewer's survey, #840); the 14 still reading 1 are the ones that survey has not reached, so
		// a 1 here means "not yet measured", NOT "starts on page 1".
		//
		// A wrong start page is silent — the PDF opens, at the wrong place, and looks like a mapping that
		// works. Each real number below was cross-checked against the mapped PDF's own filename first.
		// Books whose text has no source PDF in the Anya set are intentionally left unmapped (no button).

		// Saṅgāyana Pucchā-Vissajjanā. Multi-volume texts map to Vol 1 for now (Vol 2 → #76). e0907 has no PDF.
		addSource("e0901n.nrf.xml", SourceType.BurmeseAnya, 18, $"{BurmeseAnyaSangayana}/1. Dīghanikāya (Pu-Vi).pdf", new[] { 134, 230 });
		addSource("e0902n.nrf.xml", SourceType.BurmeseAnya, 12, $"{BurmeseAnyaSangayana}/2. Majjhimanikāya (Pu-Vi)_Vol 1.pdf");
		addSource("e0903n.nrf.xml", SourceType.BurmeseAnya, 15, $"{BurmeseAnyaSangayana}/3. Saṃyuttanikāya (Pu-Vi)_Vol 1.pdf");
		addSource("e0904n.nrf.xml", SourceType.BurmeseAnya, 20, $"{BurmeseAnyaSangayana}/4. Aṅguttaranikāya (Pu-Vi)_Vol 1.pdf");
		addSource("e0905n.nrf.xml", SourceType.BurmeseAnya, 15, $"{BurmeseAnyaSangayana}/5. Vinayapiṭaka (Pu-Vi)_Vol 1.pdf");
		addSource("e0906n.nrf.xml", SourceType.BurmeseAnya, 8, $"{BurmeseAnyaSangayana}/6. Abhidhammapiṭaka (Pu-Vi).pdf", new[] { 26, 27 });

		// Leḍī Sayāḍo. e0301 (Paramatthadīpanī) has no PDF.
		addSource("e0201n.nrf.xml", SourceType.BurmeseAnya, 112, $"{BurmeseAnyaLedi}/1. Niruttidīpanī.pdf", extraPagesAfter: new[] { 109, 109 });
		addSource("e0401n.nrf.xml", SourceType.BurmeseAnya, 8, $"{BurmeseAnyaLedi}/3. Anudīpanīpāṭha.pdf", new[] { 114, 115 }, extraPagesAfter: new[] { 85, 85 });
		addSource("e0501n.nrf.xml", SourceType.BurmeseAnya, 176, $"{BurmeseAnyaLedi}/4. Paṭṭhānuddesa dīpanīpāṭha_printed.pdf");

		// Buddha-vandanā. e0603–e0608 have no PDF.
		addSource("e0601n.nrf.xml", SourceType.BurmeseAnya, 14, $"{BurmeseAnyaBuddhaVandana}/1. Namakkāraṭīkā.pdf");
		addSource("e0602n.nrf.xml", SourceType.BurmeseAnya, 4, $"{BurmeseAnyaBuddhaVandana}/2. Mahāpaṇāmapāṭha.pdf", new[] { 53, 115, 177 });

		// Vaṃsa. e0701 (Cūḷagandhavaṃsa) has no PDF. e0703 (Mahāvaṃsa) is multi-vol → Vol 1.
		addSource("e0702n.nrf.xml", SourceType.BurmeseAnya, 16, $"{BurmeseAnyaVamsa}/3. Sāsanavaṃsappadīpikā.pdf", extraPagesAfter: new[] { 161, 161 });
		addSource("e0703n.nrf.xml", SourceType.BurmeseAnya, 7, $"{BurmeseAnyaVamsa}/2. Mahāvaṃsa (Vol-1).pdf", new[] { 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 253 });

		// Byākaraṇa (grammar). e0808/e0809/e0811 share one combined PDF (offsets to distinguish → deferred).
		// e0812 (Subodhālaṅkāraṭīkā) has no PDF.
		addSource("e0801n.nrf.xml", SourceType.BurmeseAnya, 17, $"{BurmeseAnyaByakarana}/2. Moggallānabyākaraṇaṃ.pdf");
		addSource("e0802n.nrf.xml", SourceType.BurmeseAnya, 17, $"{BurmeseAnyaByakarana}/1. Kaccāyanabyākaraṇaṃ_1990.pdf");
		addSource("e0803n.nrf.xml", SourceType.BurmeseAnya, 5, $"{BurmeseAnyaByakarana}/3. Saddanītippakaraṇaṃ (padamālā).pdf");
		addSource("e0804n.nrf.xml", SourceType.BurmeseAnya, 8, $"{BurmeseAnyaByakarana}/4. Saddanītippakaraṇaṃ (dhātumālā).pdf", new[] { 374 }, extraPagesAfter: new[] { 269, 269, 352 });
		addSource("e0805n.nrf.xml", SourceType.BurmeseAnya, 36, $"{BurmeseAnyaByakarana}/6. Padarūpasiddhi.pdf");
		addSource("e0806n.nrf.xml", SourceType.BurmeseAnya, 4, $"{BurmeseAnyaByakarana}/7. Moggallānapañcikā.pdf");
		addSource("e0807n.nrf.xml", SourceType.BurmeseAnya, 17, $"{BurmeseAnyaByakarana}/8. Payogasiddhipāṭha.pdf");
		addSource("e0808n.nrf.xml", SourceType.BurmeseAnya, 249, $"{BurmeseAnyaByakarana}/9. Vuttodaya 10. Abhidhānappadīpikāpātha 12. Subodhālaṅkāro.pdf");
		addSource("e0809n.nrf.xml", SourceType.BurmeseAnya, 9, $"{BurmeseAnyaByakarana}/9. Vuttodaya 10. Abhidhānappadīpikāpātha 12. Subodhālaṅkāro.pdf");
		addSource("e0810n.nrf.xml", SourceType.BurmeseAnya, 21, $"{BurmeseAnyaByakarana}/11. Abhidhānappadīpikāṭīkā.pdf");
		addSource("e0811n.nrf.xml", SourceType.BurmeseAnya, 213, $"{BurmeseAnyaByakarana}/9. Vuttodaya 10. Abhidhānappadīpikāpātha 12. Subodhālaṅkāro.pdf");
		addSource("e0813n.nrf.xml", SourceType.BurmeseAnya, 33, $"{BurmeseAnyaByakarana}/14. Bālāvatāra gaṇṭhipadatthavinicchayasāra.pdf", new[] { 69 }, extraPagesAfter: new[] { 116, 116 });

		// Nīti. e1002 (Nītimañjarī), e1005 (Lokanīti), e1006 (Suttantanīti) have no PDF.
		addSource("e1001n.nrf.xml", SourceType.BurmeseAnya, 31, $"{BurmeseAnyaNiti}/6. Kavidappaṇanīti.pdf");
		addSource("e1003n.nrf.xml", SourceType.BurmeseAnya, 3, $"{BurmeseAnyaNiti}/5. Dhammanīti.pdf");
		addSource("e1004n.nrf.xml", SourceType.BurmeseAnya, 7, $"{BurmeseAnyaNiti}/4. Mahārahanīti.pdf");
		addSource("e1007n.nrf.xml", SourceType.BurmeseAnya, 9, $"{BurmeseAnyaNiti}/3. Sūrassatinīti.pdf", new[] { 20, 21, 25, 26 }, extraPagesAfter: new[] { 11, 11, 11, 103, 103 });
		addSource("e1008n.nrf.xml", SourceType.BurmeseAnya, 5, $"{BurmeseAnyaNiti}/10. Cāṇakyanīti.pdf", extraPagesAfter: new[] { 19, 19, 25, 25, 29, 29, 43, 43, 47, 47 });
		addSource("e1009n.nrf.xml", SourceType.BurmeseAnya, 7, $"{BurmeseAnyaNiti}/8. Naradakkhadīpanī.pdf");
		addSource("e1010n.nrf.xml", SourceType.BurmeseAnya, 27, $"{BurmeseAnyaNiti}/9. Caturārakkhadīpanī and Kāyapaccavekkhana.pdf");

		// Pakiṇṇaka. e1102 (Sīmavisodhanī) has no PDF.
		addSource("e1101n.nrf.xml", SourceType.BurmeseAnya, 15, $"{BurmeseAnyaPakinnaka}/1. Rasavāhinī.pdf");
		addSource("e1103n.nrf.xml", SourceType.BurmeseAnya, 10, $"{BurmeseAnyaPakinnaka}/3. Vessantaragīti.pdf");

		// Sīhaḷa. e1203 (Dāṭhāvaṃsa) + e1205 (Dhātuvaṃsa) share one combined PDF. The other 11 texts have no PDF.
		// These four carry no Myanmar page markers, only ed="O", so they are mapped by their O pages. (#1100)
		// The O numbers are not this scan's pagination: e1201 opens on the right page at O 1 but drifts by
		// up to 11 pages further in (#1100). The fix is Myanmar page markers in the XML, deferred.
		// e1209's PDF is a Burmese edition with Burmese and English translations: it omits the XML's verse 1
		// (laṅkissaro jayatu…), so its verse N is the XML's N+1, and its pages run far longer than the O pages.
		addSource("e1201n.nrf.xml", SourceType.BurmeseAnya, 48, $"{BurmeseAnyaSihala}/1. Moggallāna vuttivivaraṇapañcikā (with Moggallānabyākaraṇaṃ).pdf", pageEdition: PageEdition.Other);
		addSource("e1203n.nrf.xml", SourceType.BurmeseAnya, 15, $"{BurmeseAnyaSihala}/3. and 5. Dāṭhāvaṃsa_Dhātuvaṃsa.pdf", pageEdition: PageEdition.Other);
		addSource("e1205n.nrf.xml", SourceType.BurmeseAnya, 145, $"{BurmeseAnyaSihala}/3. and 5. Dāṭhāvaṃsa_Dhātuvaṃsa.pdf", pageEdition: PageEdition.Other);
		addSource("e1209n.nrf.xml", SourceType.BurmeseAnya, 20, $"{BurmeseAnyaSihala}/9. Telakaṭāhagāthā Pali Burmese English.pdf", pageEdition: PageEdition.Other);

		// Repaired PDFs (#1136). Each repaired scan has a placeholder page for every print page the scan lacks,
		// in print order, and drops pages scanned twice. Its mapping goes under its own hash and is the latest;
		// addSourceVersions lists the original and every earlier SharePoint version with the same pages, which
		// keep the addSource mapping above. Books on a repaired PDF whose mapping is unchanged have no entries.
		// Not every repeat was dropped: vin05t (10 after 111), e0804n (one copy of 269) and e1007n (11 and 103)
		// keep theirs, as ExtraPagesAfter in their latest mappings. A later version that drops them changes those
		// books' pages, so it needs an addVersion of its own: without one it would get these mappings, and open
		// pages late. Likewise the
		// truncated SharePoint uploads (1957 Jātaka-aṭṭhakathā-6 v1-v2, 2010 Khuddaka v1-v2) are listed with the
		// originals: offline, the app still serves a cached copy that is not intact. (#1136 review)
		// 1957 edition/1 - Mula - Vinaya/Cūḷavaggapāḷi.pdf
		addSourceVersions("vin02m3.mul.xml", SourceType.Burmese1957, "Wsa4s3SaRHbMo8LdD1OqQO3trbw=");
		addVersion("vin02m3.mul.xml", SourceType.Burmese1957, new[] { "T0oYCRsot789+NYr+2B6nTmBqz8=" }, 11, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Apadānapāḷi–2- Buddhavaṃsa- Cariyāpiṭakapāḷi.pdf
		addSourceVersions("s0510m2.mul.xml", SourceType.Burmese1957, "E8/CNp8yd4luMMTq+HgV7G2x7zc=");
		addVersion("s0510m2.mul.xml", SourceType.Burmese1957, new[] { "38dsMrQYiSp9vxAU8/Zu+fPvv7M=" }, 16, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Apadānapāḷi–2- Buddhavaṃsa- Cariyāpiṭakapāḷi.pdf
		addSourceVersions("s0511m.mul.xml", SourceType.Burmese1957, "E8/CNp8yd4luMMTq+HgV7G2x7zc=");
		addVersion("s0511m.mul.xml", SourceType.Burmese1957, new[] { "38dsMrQYiSp9vxAU8/Zu+fPvv7M=" }, 16, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Apadānapāḷi–2- Buddhavaṃsa- Cariyāpiṭakapāḷi.pdf
		addSourceVersions("s0512m.mul.xml", SourceType.Burmese1957, "E8/CNp8yd4luMMTq+HgV7G2x7zc=");
		addVersion("s0512m.mul.xml", SourceType.Burmese1957, new[] { "38dsMrQYiSp9vxAU8/Zu+fPvv7M=" }, 16, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Jātakapāḷi-1.pdf
		addSourceVersions("s0513m.mul.xml", SourceType.Burmese1957, "JHu2HztSutryWinBV/EVq75OmAo=");
		addVersion("s0513m.mul.xml", SourceType.Burmese1957, new[] { "j3fyX2ya3OYAauGwibJ5eXHbhII=" }, 27, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Khuddakapāṭha-Dhammapada-Udāna-Itivuttaka-Suttanipātapāḷi.pdf: placeholders for
		// the blank print 12 and 194 (between texts) and 276-278 make one page run, so all five books start at 25.
		addSourceVersions("s0502m.mul.xml", SourceType.Burmese1957, "I8IsbBn+WIN6z+wMaSDL1q1wUTw=", "+a/0OYN6wnSnRFDAQuOQcAB56cE=");
		addVersion("s0502m.mul.xml", SourceType.Burmese1957, new[] { "Dlm9hZQ61Yc56xLsWtGTx2uFZbo=" }, 25, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		addSourceVersions("s0503m.mul.xml", SourceType.Burmese1957, "I8IsbBn+WIN6z+wMaSDL1q1wUTw=", "+a/0OYN6wnSnRFDAQuOQcAB56cE=");
		addVersion("s0503m.mul.xml", SourceType.Burmese1957, new[] { "Dlm9hZQ61Yc56xLsWtGTx2uFZbo=" }, 25, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		addSourceVersions("s0504m.mul.xml", SourceType.Burmese1957, "I8IsbBn+WIN6z+wMaSDL1q1wUTw=", "+a/0OYN6wnSnRFDAQuOQcAB56cE=");
		addVersion("s0504m.mul.xml", SourceType.Burmese1957, new[] { "Dlm9hZQ61Yc56xLsWtGTx2uFZbo=" }, 25, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		addSourceVersions("s0505m.mul.xml", SourceType.Burmese1957, "I8IsbBn+WIN6z+wMaSDL1q1wUTw=", "+a/0OYN6wnSnRFDAQuOQcAB56cE=");
		addVersion("s0505m.mul.xml", SourceType.Burmese1957, new[] { "Dlm9hZQ61Yc56xLsWtGTx2uFZbo=" }, 25, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Mahāniddesapāḷi.pdf
		addSourceVersions("s0515m.mul.xml", SourceType.Burmese1957, "4pD0FRbn9EA1vSVlqjBx9PchV7o=");
		addVersion("s0515m.mul.xml", SourceType.Burmese1957, new[] { "Hb3n6EXa8p04JCwpShEnPeCcyIY=" }, 5, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Netti-Peṭakopadesapāḷi.pdf
		addSourceVersions("s0519m.mul.xml", SourceType.Burmese1957, "KDPvQcdMDOfQIYUTt1icAwdNXlY=");
		addVersion("s0519m.mul.xml", SourceType.Burmese1957, new[] { "VCV4d/o6SeKVc2sGfL85OddUWcY=" }, 7, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Netti-Peṭakopadesapāḷi.pdf
		addSourceVersions("s0520m.nrf.xml", SourceType.Burmese1957, "KDPvQcdMDOfQIYUTt1icAwdNXlY=");
		addVersion("s0520m.nrf.xml", SourceType.Burmese1957, new[] { "VCV4d/o6SeKVc2sGfL85OddUWcY=" }, 7, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Sagāthāvagga-Nidānavaggasaṃyuttapāḷi.pdf
		addSourceVersions("s0301m.mul.xml", SourceType.Burmese1957, "eG7qLjxIdO+MgT7alwe1QfSovek=");
		addVersion("s0301m.mul.xml", SourceType.Burmese1957, new[] { "cDTg3BkOMaoVSusWg+pcIYaGjKk=" }, 38, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/2 - Mula - Sutta/Sagāthāvagga-Nidānavaggasaṃyuttapāḷi.pdf
		addSourceVersions("s0302m.mul.xml", SourceType.Burmese1957, "eG7qLjxIdO+MgT7alwe1QfSovek=");
		addVersion("s0302m.mul.xml", SourceType.Burmese1957, new[] { "cDTg3BkOMaoVSusWg+pcIYaGjKk=" }, 38, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/3 - Mula - Abhidhamma/Paṭṭhānapāḷi-3.pdf
		addSourceVersions("abh03m9.mul.xml", SourceType.Burmese1957, "utVPF2dh9ZFxOYrJM8AyS3Vi9UU=");
		addVersion("abh03m9.mul.xml", SourceType.Burmese1957, new[] { "MwXHO98dzkk+Aw/KiODIpGJ6fbg=" }, 7, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/3 - Mula - Abhidhamma/Paṭṭhānapāḷi-4.pdf
		addSourceVersions("abh03m10.mul.xml", SourceType.Burmese1957, "kJ8ZWZngW+g+jP8gjRs5S7HPCLM=");
		addVersion("abh03m10.mul.xml", SourceType.Burmese1957, new[] { "k0iZ5/EFRCCJ9M/HeC/LPS1gbiQ=" }, 10, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/3 - Mula - Abhidhamma/Paṭṭhānapāḷi-5.pdf
		addSourceVersions("abh03m11.mul.xml", SourceType.Burmese1957, "rqSNenXbNbqz1zlXD10lA4rNFDk=");
		addVersion("abh03m11.mul.xml", SourceType.Burmese1957, new[] { "qBwk3Z4/Cpfc+mEXzCxd+cvEiRc=" }, 6, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/3 - Mula - Abhidhamma/Yamakapāḷi-1.pdf
		addSourceVersions("abh03m4.mul.xml", SourceType.Burmese1957, "x6uxixkVBew2hmCZNBvt8ol9tx4=");
		addVersion("abh03m4.mul.xml", SourceType.Burmese1957, new[] { "afRwzTxdz0yz3xH2oqSnHLJSr7c=" }, 10, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Apadāna-aṭṭhakathā-1.pdf
		addSourceVersions("s0510a.att.xml", SourceType.Burmese1957, "PAuljelsXWxA/z1+R96JraAakts=");
		addVersion("s0510a.att.xml", SourceType.Burmese1957, new[] { "ZZx53zzIkplk/GxjNqZeMn6nQjw=" }, 4, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Cariyāpiṭaka-aṭṭhakathā.pdf
		addSourceVersions("s0512a.att.xml", SourceType.Burmese1957, "TaADCf3Axspv5J+kRghFVUxd+UM=");
		addVersion("s0512a.att.xml", SourceType.Burmese1957, new[] { "QanA4ssdOqr3HMP+sEsvuTB+nu8=" }, 5, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Cūḷaniddesa-aṭṭhakathā Netti-aṭṭhakathā.pdf
		addSourceVersions("s0519a.att.xml", SourceType.Burmese1957, "QfXediggzuU3ELD4Ais0obmKuI4=", "lrK5egfzxACLCKL02nYs15Yuhjo=", "73CSUoFa5/v7KiRjPpk6f/JP3sw=", "jZe9JGGg9pknNWmw5dFRIFU+oOk=");
		addVersion("s0519a.att.xml", SourceType.Burmese1957, new[] { "dmzsUciIETb1wQzzaTXnUBuj2A0=" }, 181, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Duka-tika-catukkanipāta-aṭṭhakathā.pdf
		addSourceVersions("s0402a.att.xml", SourceType.Burmese1957, "vRnMOu5vP+98BTZ+y+SBf87JLZo=");
		addVersion("s0402a.att.xml", SourceType.Burmese1957, new[] { "xMMwUf3W9JrnuGjw+RCzRKS78I4=" }, 19, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-4.pdf
		addSourceVersions("s0513a4.att.xml", SourceType.Burmese1957, "iyC2HM/MBWsn0+jiLNbYJfuhIb8=");
		addVersion("s0513a4.att.xml", SourceType.Burmese1957, new[] { "jyTk3xWgD+FKqIYp5qX9HdbQp3w=" }, 7, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-6.pdf
		addSourceVersions("s0514a2.att.xml", SourceType.Burmese1957, "SbkkegXimNanqOJeMQjmihMhRFU=", "xNS9kNzYH9UfrNd2wgqBQxHWuYY=", "LhLr8yyBr7umdTED2rbiH/Ra4l0=");
		addVersion("s0514a2.att.xml", SourceType.Burmese1957, new[] { "PqM1eF24EmPY8cUqKr/5w4NRVQI=" }, 4, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Jātaka-aṭṭhakathā-7.pdf
		addSourceVersions("s0514a3.att.xml", SourceType.Burmese1957, "Dzl5/VjvCZffTZUVZJ9+UsabHPw=");
		addVersion("s0514a3.att.xml", SourceType.Burmese1957, new[] { "LlFJbfvG1c/5YybLPzi2bIgAx0A=" }, 4, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Mahāniddesa-aṭṭhakathā.pdf
		addSourceVersions("s0515a.att.xml", SourceType.Burmese1957, "gmWho2grRCE10FLiyfn7GXgIXSA=");
		addVersion("s0515a.att.xml", SourceType.Burmese1957, new[] { "lZpE0kuyG/u4p7dW23S4Peq/qVM=" }, 4, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Pañcakanipātādi-aṭṭhakathā.pdf
		addSourceVersions("s0403a.att.xml", SourceType.Burmese1957, "cw4yQ1bzr3gNUtFhjcmmSPvzzdU=");
		addVersion("s0403a.att.xml", SourceType.Burmese1957, new[] { "PwL+ktJWWBmYJ8Jwtz82dzrQLL4=" }, 27, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Pañcakanipātādi-aṭṭhakathā.pdf
		addSourceVersions("s0404a.att.xml", SourceType.Burmese1957, "cw4yQ1bzr3gNUtFhjcmmSPvzzdU=");
		addVersion("s0404a.att.xml", SourceType.Burmese1957, new[] { "PwL+ktJWWBmYJ8Jwtz82dzrQLL4=" }, 27, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Pañcappakaraṇa-aṭṭhakathā.pdf
		addSourceVersions("abh03a.att.xml", SourceType.Burmese1957, "Q/mAX1/7SgrR5lNswrbGIRWkqRo=");
		addVersion("abh03a.att.xml", SourceType.Burmese1957, new[] { "ybj/ERY5/XsosWBIxPnUsQUZWrY=" }, 13, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Petavatthu-aṭṭhakathā.pdf
		addSourceVersions("s0507a.att.xml", SourceType.Burmese1957, "6HkMg+k5Xe55tC8zVI6Z/27igXA=");
		addVersion("s0507a.att.xml", SourceType.Burmese1957, new[] { "Cx2+QUVZ+cRmp9/9jY519t6it6Y=" }, 6, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Pārājikakaṇḍa-aṭṭhakathā-1.pdf
		addSourceVersions("vin01a.att.xml", SourceType.Burmese1957, "VC5tqb7PX+RW+T2gNKQQ70TZ2UQ=");
		addVersion("vin01a.att.xml", SourceType.Burmese1957, new[] { "5JtoIlHghSt/L1OdbP6fqYrADWs=" }, 15, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Suttanipāta-aṭṭhakathā - 1.pdf
		addSourceVersions("s0505a.att.xml", SourceType.Burmese1957, "W8sDu1I3p3VO1RMC1dXJKTcAMvk=");
		addVersion("s0505a.att.xml", SourceType.Burmese1957, new[] { "4WgK6uJ2SQgTAdCetI24S1x32yo=" }, 4, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/4 - Atthakatha/Uparipaṇṇāsa-aṭṭhakathā.pdf
		addSourceVersions("s0203a.att.xml", SourceType.Burmese1957, "NjuFiKaBGD7GPOA4O+DCv4UoWhg=");
		addVersion("s0203a.att.xml", SourceType.Burmese1957, new[] { "XQGXlyc1u3SNkeqto2QKSk2atZA=" }, 6, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Khuddasikkhā-mūlasikkhā.pdf
		addSourceVersions("vin13t.nrf.xml", SourceType.Burmese1957, "Y/967oP2IctGHKHw51LVUEn8sfk=", "RwBmROq0lVfqIvdRIB8d2RPgdTg=");
		addVersion("vin13t.nrf.xml", SourceType.Burmese1957, new[] { "8dpTHx/4GZ/UuovEyXg7OWOvrvk=" }, 15, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Mohavicchedanī (abhidhammamātikā).pdf
		addSourceVersions("abh09t.nrf.xml", SourceType.Burmese1957, "NPsw+bjgFLxruwofd+WZrBaDV5Y=");
		addVersion("abh09t.nrf.xml", SourceType.Burmese1957, new[] { "hBTMAUlTe7yejYTfZjx38itNhS0=" }, 16, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Pañcakanipātādi-ṭīkā.pdf
		addSourceVersions("s0403t.tik.xml", SourceType.Burmese1957, "nC2l7DW7xRnzEDsmzHIML5w1aQk=");
		addVersion("s0403t.tik.xml", SourceType.Burmese1957, new[] { "4r/swzBJkrdwepc+g6FGaDZ6Fsc=" }, 19, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Pañcakanipātādi-ṭīkā.pdf
		addSourceVersions("s0404t.tik.xml", SourceType.Burmese1957, "nC2l7DW7xRnzEDsmzHIML5w1aQk=");
		addVersion("s0404t.tik.xml", SourceType.Burmese1957, new[] { "4r/swzBJkrdwepc+g6FGaDZ6Fsc=" }, 19, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Pañcappakaraṇa-mūlaṭīkā-anuṭīkā.pdf
		addSourceVersions("abh03t.tik.xml", SourceType.Burmese1957, "Z7V7Qap5xZ9lF7DsIPf+3vs3UPE=");
		addVersion("abh03t.tik.xml", SourceType.Burmese1957, new[] { "xfiHVxwn5aWXd/pHxIkivff14uk=" }, 20, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Pañcappakaraṇa-mūlaṭīkā-anuṭīkā.pdf
		addSourceVersions("abh05t.nrf.xml", SourceType.Burmese1957, "Z7V7Qap5xZ9lF7DsIPf+3vs3UPE=");
		addVersion("abh05t.nrf.xml", SourceType.Burmese1957, new[] { "xfiHVxwn5aWXd/pHxIkivff14uk=" }, 288, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Sāratthadīpanī-ṭīkā-3.pdf
		addSourceVersions("vin02t.tik.xml", SourceType.Burmese1957, "XoIiKjAqkZuEvpWl2q24op2bYgQ=");
		addVersion("vin02t.tik.xml", SourceType.Burmese1957, new[] { "zBDUuJJsGMZKZTRgqKrwjeMW9DM=" }, 21, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Vajirabuddhi-ṭīkā.pdf
		addSourceVersions("vin06t.nrf.xml", SourceType.Burmese1957, "jHzsEm2gqtxQGGLOYdmYufKqJcw=");
		addVersion("vin06t.nrf.xml", SourceType.Burmese1957, new[] { "NxB4zq+Bem/5jHiy+r+wzpvBc7I=" }, 23, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 1957 edition/5 - Tika_/Vinayasaṅgahaṭṭhakathā.pdf
		addSourceVersions("vin05t.nrf.xml", SourceType.Burmese1957, "Sr8VouZuPBV0MTqzkNMuduM3iQk=");
		addVersion("vin05t.nrf.xml", SourceType.Burmese1957, new[] { "jL6BdyJ2r9SQmhYSqAY2I2NPPDM=" }, 6, missingPages: null, extraPagesAfter: new[] { 111, 111, 111, 111, 111, 111, 111, 111, 111, 111 }, pageOverrides: null);
		// 1957 edition/5 - Tika_/Kaṅkhāvitaraṇī purāṇa-abhinava-ṭīkā.pdf
		addSourceVersions("vin09t.nrf.xml", SourceType.Burmese1957, "Vc6wpHw0qBAwVDGvqsQB+U7dy4E=");
		addVersion("vin09t.nrf.xml", SourceType.Burmese1957, new[] { "bw5rmQnByMUxjO8bO+lz2LGYckM=" }, 19, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/011-Uparipaṇṇāsapāḷi.pdf
		addSourceVersions("s0203m.mul.xml", SourceType.Burmese2010, "vyARXtNNTaOQ1IuU10tho7UxsTU=");
		addVersion("s0203m.mul.xml", SourceType.Burmese2010, new[] { "TXEHVD81puhAyecc5ncb56EH19c=" }, 11, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/019-Khuddakapāṭha-Dhammapada-Udāna-Itivuttaka-Suttanipātapāḷi.pdf
		addSourceVersions("s0504m.mul.xml", SourceType.Burmese2010, "Tmvo85sbxXFyx58jEDFs37LIQOA=", "eQdF45DdbCF9pGZg3BW8lieHErE=", "n2SGHHE7VRYVs3h2sL+9uq5stbA=", "Nvtz/p0dz8g07VXosjAFml3OlrM=", "PgibTf5ftbHjtC0o3RC3tB3r75I=");
		addVersion("s0504m.mul.xml", SourceType.Burmese2010, new[] { "MKbkJRWUv/3aXrbjxbMebGWlBME=" }, 29, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/019-Khuddakapāṭha-Dhammapada-Udāna-Itivuttaka-Suttanipātapāḷi.pdf
		addSourceVersions("s0505m.mul.xml", SourceType.Burmese2010, "Tmvo85sbxXFyx58jEDFs37LIQOA=", "eQdF45DdbCF9pGZg3BW8lieHErE=", "n2SGHHE7VRYVs3h2sL+9uq5stbA=", "Nvtz/p0dz8g07VXosjAFml3OlrM=", "PgibTf5ftbHjtC0o3RC3tB3r75I=");
		addVersion("s0505m.mul.xml", SourceType.Burmese2010, new[] { "MKbkJRWUv/3aXrbjxbMebGWlBME=" }, 29, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/020-Vimānavatthu-PetavatthuTheragāthā-Therīgāthāpāḷi.pdf
		addSourceVersions("s0506m.mul.xml", SourceType.Burmese2010, "sRg7hQPopGDGTWUoJbtFte/8w1Q=", "V4T4V+I9Z9pMLbi5ZquS8cGcLYk=", "Ty8y64L5jpjZ3HCiMeXAiW/0J5A=", "56bDM0uInAlmOKNQWQLEKu1KFv8=", "0QWooHkZoT5KTJsjCLSnCo7FYLw=");
		addVersion("s0506m.mul.xml", SourceType.Burmese2010, new[] { "lDVq1QDHmETt0Rw+B7M6NVo03fw=" }, 21, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/020-Vimānavatthu-PetavatthuTheragāthā-Therīgāthāpāḷi.pdf
		addSourceVersions("s0507m.mul.xml", SourceType.Burmese2010, "sRg7hQPopGDGTWUoJbtFte/8w1Q=", "V4T4V+I9Z9pMLbi5ZquS8cGcLYk=", "Ty8y64L5jpjZ3HCiMeXAiW/0J5A=", "56bDM0uInAlmOKNQWQLEKu1KFv8=", "0QWooHkZoT5KTJsjCLSnCo7FYLw=");
		addVersion("s0507m.mul.xml", SourceType.Burmese2010, new[] { "lDVq1QDHmETt0Rw+B7M6NVo03fw=" }, 21, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/020-Vimānavatthu-PetavatthuTheragāthā-Therīgāthāpāḷi.pdf
		addSourceVersions("s0508m.mul.xml", SourceType.Burmese2010, "sRg7hQPopGDGTWUoJbtFte/8w1Q=", "V4T4V+I9Z9pMLbi5ZquS8cGcLYk=", "Ty8y64L5jpjZ3HCiMeXAiW/0J5A=", "56bDM0uInAlmOKNQWQLEKu1KFv8=", "0QWooHkZoT5KTJsjCLSnCo7FYLw=");
		addVersion("s0508m.mul.xml", SourceType.Burmese2010, new[] { "lDVq1QDHmETt0Rw+B7M6NVo03fw=" }, 21, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/020-Vimānavatthu-PetavatthuTheragāthā-Therīgāthāpāḷi.pdf
		addSourceVersions("s0509m.mul.xml", SourceType.Burmese2010, "sRg7hQPopGDGTWUoJbtFte/8w1Q=", "V4T4V+I9Z9pMLbi5ZquS8cGcLYk=", "Ty8y64L5jpjZ3HCiMeXAiW/0J5A=", "56bDM0uInAlmOKNQWQLEKu1KFv8=", "0QWooHkZoT5KTJsjCLSnCo7FYLw=");
		addVersion("s0509m.mul.xml", SourceType.Burmese2010, new[] { "lDVq1QDHmETt0Rw+B7M6NVo03fw=" }, 21, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/029-Dhammasaṅgaṇīpāḷi.pdf
		addSourceVersions("abh01m.mul.xml", SourceType.Burmese2010, "HR5T56+grlF+TULiozPOO+VBsEM=");
		addVersion("abh01m.mul.xml", SourceType.Burmese2010, new[] { "vTzMFbxHvMtnL8VheNhimfohLT4=" }, 23, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/036-Paṭṭhānapāḷi-1.pdf
		addSourceVersions("abh03m7.mul.xml", SourceType.Burmese2010, "PTSvuIPZ/q/Rk4DPw0xSFQrYT2s=");
		addVersion("abh03m7.mul.xml", SourceType.Burmese2010, new[] { "WU1qyNKQdpxeZPyQSLH7BqvXZXM=" }, 35, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/037-Paṭṭhānapāḷi-2.pdf
		addSourceVersions("abh03m8.mul.xml", SourceType.Burmese2010, "3Vq++SgDO6wh73dfqs/P+/bDjdk=");
		addVersion("abh03m8.mul.xml", SourceType.Burmese2010, new[] { "hTI/J32QB2fIMv+QAdjB652+sOI=" }, 31, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// 2010 edition/Mula/040-Paṭṭhānapāḷi-5.pdf
		addSourceVersions("abh03m11.mul.xml", SourceType.Burmese2010, "RQ3gNXYmLJQmVPd5o383+L23qII=");
		addVersion("abh03m11.mul.xml", SourceType.Burmese2010, new[] { "XtDswq3YrGaghc9RlIb+m3w7b/g=" }, 11, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/2. Saṅgāyana-puccha vissajjanā/1. Dīghanikāya (Pu-Vi).pdf
		addSourceVersions("e0901n.nrf.xml", SourceType.BurmeseAnya, "wi9p98or4JYf47zyrrK+Awm/jx4=");
		addVersion("e0901n.nrf.xml", SourceType.BurmeseAnya, new[] { "2NZqnblh5LZrSvMQ/do4A+0G5Cc=" }, 18, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/2. Saṅgāyana-puccha vissajjanā/6. Abhidhammapiṭaka (Pu-Vi).pdf
		addSourceVersions("e0906n.nrf.xml", SourceType.BurmeseAnya, "rNJLm3V771H28FutWP/RPT5DG+k=");
		addVersion("e0906n.nrf.xml", SourceType.BurmeseAnya, new[] { "EiQgmqyNruX07deNYJFaNP119GY=" }, 8, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/3. Leḍī sayāḍo gantha-saṅgaho/1. Niruttidīpanī.pdf
		addSourceVersions("e0201n.nrf.xml", SourceType.BurmeseAnya, "0C9xqa9G8qKkIXOUnRTEMGGR+K4=", "iOrxgHX/kcGPf/fmSVxETowmbB4=");
		addVersion("e0201n.nrf.xml", SourceType.BurmeseAnya, new[] { "p6/u4+7eChnup1mC91gGjmKCzwI=" }, 112, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/3. Leḍī sayāḍo gantha-saṅgaho/3. Anudīpanīpāṭha.pdf
		addSourceVersions("e0401n.nrf.xml", SourceType.BurmeseAnya, "Hry8vsD4gboGIVKllmSqRgJ11RY=");
		addVersion("e0401n.nrf.xml", SourceType.BurmeseAnya, new[] { "2xYNGLOjue5Lm6/mbym0Jm1oYyk=" }, 8, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/4. Buddha-vandanā gantha-saṅgaho/2. Mahāpaṇāmapāṭha.pdf
		addSourceVersions("e0602n.nrf.xml", SourceType.BurmeseAnya, "znxWIk825IzVvP9XXXp9mfPmbjs=");
		addVersion("e0602n.nrf.xml", SourceType.BurmeseAnya, new[] { "zwtFrcpugplrWn1Ajn20MhJ7a+8=" }, 4, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/5. Vaṃsa-gantha-saṅgaho/2. Mahāvaṃsa (Vol-1).pdf
		addSourceVersions("e0703n.nrf.xml", SourceType.BurmeseAnya, "OB2u2HVIFLEvYPr3bJ/eZSjFZqU=");
		addVersion("e0703n.nrf.xml", SourceType.BurmeseAnya, new[] { "uBf/vd4TenEO5I00YLCQfZMwxDo=" }, 7, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/5. Vaṃsa-gantha-saṅgaho/3. Sāsanavaṃsappadīpikā.pdf
		addSourceVersions("e0702n.nrf.xml", SourceType.BurmeseAnya, "m+5v4AG4s4WbEQEpPQ/ggwDpaT8=");
		addVersion("e0702n.nrf.xml", SourceType.BurmeseAnya, new[] { "J5OdLtWEosbV64Yc2eVwbzQwiLs=" }, 16, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/6. Byākaraṇa gantha-saṅgaho/14. Bālāvatāra gaṇṭhipadatthavinicchayasāra.pdf
		addSourceVersions("e0813n.nrf.xml", SourceType.BurmeseAnya, "2SPnZE1BbY8ZleBOrVCALD/1Yhg=");
		addVersion("e0813n.nrf.xml", SourceType.BurmeseAnya, new[] { "rxRN4wHckH93esplANU/OzBwSRc=" }, 33, missingPages: null, extraPagesAfter: null, pageOverrides: null);
		// Anya/6. Byākaraṇa gantha-saṅgaho/4. Saddanītippakaraṇaṃ (dhātumālā).pdf
		addSourceVersions("e0804n.nrf.xml", SourceType.BurmeseAnya, "zI/GF7EHnBfbh6SfcvqvJptCPX8=");
		addVersion("e0804n.nrf.xml", SourceType.BurmeseAnya, new[] { "qjHIV9oFyJVgELu52AQWNaSdm50=" }, 8, missingPages: null, extraPagesAfter: new[] { 269 }, pageOverrides: null);
		// Anya/7. Nīti-gantha-saṅgaho/3. Sūrassatinīti.pdf
		addSourceVersions("e1007n.nrf.xml", SourceType.BurmeseAnya, "LyVwC68Wq3AeHnx2dIliFNh8NUc=");
		addVersion("e1007n.nrf.xml", SourceType.BurmeseAnya, new[] { "1pX1kvcjKRVcGSk08QzK3u6I9x4=" }, 9, missingPages: null, extraPagesAfter: new[] { 11, 11, 11, 103, 103 }, pageOverrides: null);
	}

	private void addSource(string filename, SourceType sourceType, int pageStart, string path,
		int[]? missingPages = null, PageEdition pageEdition = PageEdition.Myanmar, int[]? extraPagesAfter = null,
		Dictionary<int, int>? pageOverrides = null)
	{
		if (!sourcesMap.TryGetValue(filename, out var d))
		{
			d = new Dictionary<SourceType, Source>();
			sourcesMap[filename] = d;
		}
		d[sourceType] = new Source(sourceType, pageStart, path, missingPages, pageEdition, extraPagesAfter, pageOverrides);
	}

	/// <summary>
	/// The versions of a book's PDF that its <see cref="addSource"/> mapping fits, by SharePoint
	/// <c>quickXorHash</c>. Needed only once the book also has an <see cref="addVersion"/> entry: a file whose
	/// version is not listed gets the latest mapping, so every version the addSource mapping was measured
	/// against (and any older copy with the same pages) must be listed here, or a reader with that copy
	/// cached would get the newer mapping. The app logs each PDF's hash when it opens one ("PDF version …").
	/// Call after the book's addSource. (#1136)
	/// </summary>
	private void addSourceVersions(string filename, SourceType sourceType, params string[] quickXorHashes)
	{
		var main = GetSource(filename, sourceType)
			?? throw new InvalidOperationException($"addSourceVersions for {filename} {sourceType} before its addSource");
		main.AddVersion(quickXorHashes, main, latest: false);
	}

	/// <summary>
	/// A later version of a book's PDF whose pages differ from the earlier versions', with its own mapping,
	/// by SharePoint <c>quickXorHash</c>. The last one listed for a book is its latest mapping, which any
	/// version with no mapping of its own gets: [fsnow, 2026-11-26] "If there is a newer file we don't
	/// have a mapping for, we will use the latest." Call after the book's addSource and addSourceVersions,
	/// oldest version first. (#1136)
	/// </summary>
	private void addVersion(string filename, SourceType sourceType, string[] quickXorHashes, int pageStart,
		int[]? missingPages = null, int[]? extraPagesAfter = null, Dictionary<int, int>? pageOverrides = null)
	{
		var main = GetSource(filename, sourceType)
			?? throw new InvalidOperationException($"addVersion for {filename} {sourceType} before its addSource");
		main.AddVersion(quickXorHashes,
			new Source(sourceType, pageStart, main.Path, missingPages, main.PageEdition, extraPagesAfter, pageOverrides),
			latest: true);
	}

	public Source? GetSource(string filename, SourceType sourceType)
	{
		if (sourcesMap.TryGetValue(filename, out var d) && d.TryGetValue(sourceType, out var source))
			return source;
		return null;
	}

	public enum SourceType
	{
		Burmese1957,
		Burmese2010,
		VriPrint,
		// Anya (extra-canonical) texts have a single Burmese source set that is NOT split into dated
		// editions (see _Source/01 - Burmese-CST/Anya, a sibling of the "1957 edition" / "2010 edition"
		// folders). They surface as a single "PDF" button rather than "1957" / "2010". (#418)
		BurmeseAnya
	}

	// Fixed display order for a book's source buttons (dated editions first, then the undated Anya PDF).
	private static readonly SourceType[] sourceOrder =
		{ SourceType.Burmese1957, SourceType.Burmese2010, SourceType.VriPrint, SourceType.BurmeseAnya };

	/// <summary>
	/// The source editions available for a book, in display order. Empty if the book has no source PDF.
	/// The UI renders one button per entry (labelled via <see cref="GetSourceLabel"/>).
	/// </summary>
	public IReadOnlyList<SourceType> GetAvailableSources(string filename)
	{
		if (!sourcesMap.TryGetValue(filename, out var d))
			return System.Array.Empty<SourceType>();
		var result = new List<SourceType>();
		foreach (var st in sourceOrder)
			if (d.ContainsKey(st))
				result.Add(st);
		return result;
	}

	/// <summary>Short toolbar-button label for a source type ("1957" / "2010" / "PDF").</summary>
	public static string GetSourceLabel(SourceType sourceType) => sourceType switch
	{
		SourceType.Burmese1957 => "1957",
		SourceType.Burmese2010 => "2010",
		SourceType.VriPrint => "VRI",
		SourceType.BurmeseAnya => "PDF",
		_ => sourceType.ToString()
	};

	public class Source
	{
		public Source(SourceType sourceType, int pageStart, string path, int[]? missingPages = null,
			PageEdition pageEdition = PageEdition.Myanmar, int[]? extraPagesAfter = null,
			IReadOnlyDictionary<int, int>? pageOverrides = null)
		{
			PageOverrides = pageOverrides ?? new Dictionary<int, int>();
			ExtraPagesAfter = extraPagesAfter ?? Array.Empty<int>();
			PageEdition = pageEdition;
			SourceType = sourceType;
			PageStart = pageStart;
			Path = path;
			MissingPages = missingPages ?? Array.Empty<int>();
		}

		/// <summary>
		/// Relative path within SharePoint's _Source folder.
		/// Use with ISharePointService.GetPdfAsync().
		/// </summary>
		public string Path { get; set; }

		public SourceType SourceType { get; set; }

		/// <summary>
		/// The page numbering whose page numbers are this PDF's printed pages: what <see cref="PageStart"/>,
		/// <see cref="MissingPages"/> and <see cref="PdfPageFor"/> count in. Myanmar for every book except
		/// the four Sīhaḷa books (e1201, e1203, e1205, e1209), which have no Myanmar page markers and are
		/// mapped by their ed="O" pages. (#1100)
		/// </summary>
		public PageEdition PageEdition { get; }

		public int PageStart { get; set; }

		/// <summary>
		/// Print page numbers that exist in the printed volume but have NO page in this PDF — pages
		/// the scanner passed over. Ascending. (#540) Mostly blank pages, but not always: the 1957
		/// Netti scan (s0519m) lacks print pages 156-157, which carry text.
		///
		/// <para>Without this, one skipped page throws off every page after it, and the error accumulates:
		/// s0402a has blanks at 68 and 248, so Tikanipāta landed one page late and Catukkanipāta two.</para>
		///
		/// <para>PER PDF, not per book. The seed values were derived from gaps in the XML page-break
		/// markers — a blank page has no text, so it leaves no marker AND nothing to scan — but the
		/// scanning was done by a different group within VRI from the one that produced the texts, so
		/// the two Burmese editions may well skip different pages. Correct an entry when QA finds an
		/// off-by-one; do not assume the paired edition needs the same correction.</para>
		/// </summary>
		public int[] MissingPages { get; set; }

		/// <summary>
		/// The counterpart of <see cref="MissingPages"/>: one entry per EXTRA page in this PDF, giving the
		/// print page it follows. Every print page after it opens one PDF page later. Ascending; a value may
		/// repeat. The 1957 abh03m8 scan runs 93, 94, 95, 94, 95, 96, so its entry is { 95, 95 }: print pages
		/// 94 and 95 open their first copies and 96 onward open two pages later.
		/// </summary>
		public int[] ExtraPagesAfter { get; set; }

		/// <summary>
		/// Print page → PDF page, for pages the scan holds out of order, which no count of missing and
		/// extra pages can place. It wins over the computed page and moves only the pages it names. The
		/// 1957 vin13t scan runs 44, 45, 48, 47, 49 (46 absent), so its entry is { 47 → 61, 48 → 60 }.
		/// If the PDF itself is reordered, the reordered version gets an addVersion entry without this one,
		/// and the scan it replaced keeps this one (#1136).
		/// </summary>
		public IReadOnlyDictionary<int, int> PageOverrides { get; }

		private readonly Dictionary<string, Source> versions = new();
		private Source? latest;

		/// <summary>
		/// The mapping for each listed version of this PDF, by SharePoint <c>quickXorHash</c>: this one for the
		/// versions in <c>addSourceVersions</c>, and each <c>addVersion</c> entry's own. (#1136)
		/// </summary>
		public IReadOnlyDictionary<string, Source> Versions => versions;

		/// <summary>The mapping a version with none of its own gets: the last addVersion entry, or this one.</summary>
		public Source Latest => latest ?? this;

		internal void AddVersion(IEnumerable<string> quickXorHashes, Source mapping, bool latest)
		{
			foreach (var hash in quickXorHashes)
				if (!versions.TryAdd(hash, mapping))
					throw new InvalidOperationException($"Two mappings for version {hash} of {Path}");
			if (latest)
				this.latest = mapping;
		}

		/// <summary>
		/// The mapping for the version of the PDF with this hash: its own, if it is listed, otherwise the
		/// latest. A null hash (the version could not be read) also gets the latest.
		/// </summary>
		public Source ForVersion(string? quickXorHash) =>
			quickXorHash != null && versions.TryGetValue(quickXorHash, out var v) ? v : Latest;

		/// <summary>
		/// PDF page for a printed page number, accounting for pages absent from the scan and pages
		/// the scan holds twice. This is the mapping of the PDF version this Source describes; for the
		/// version on disk, call <see cref="ForVersion"/> first.
		/// </summary>
		public int PdfPageFor(int printPage)
		{
			if (PageOverrides.TryGetValue(printPage, out int overridden))
				return overridden;
			int skipped = 0;
			foreach (int p in MissingPages)
			{
				if (p >= printPage) break;   // MissingPages is ascending
				skipped++;
			}
			int extra = 0;
			foreach (int p in ExtraPagesAfter)
			{
				if (p >= printPage) break;   // ExtraPagesAfter is ascending
				extra++;
			}
			int pdfPage = PageStart + (printPage - 1) - skipped + extra;
			return pdfPage < 1 ? 1 : pdfPage;
		}
	}
}
