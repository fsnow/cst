using System;
using System.Collections;
using System.Collections.Generic;

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
	private const string Att_Vin_Parajika2_1957 = "Pārājikakaṇḍa-aṭṭhakathā-2.pdf"; // TODO(#76): vol-2 mapping pending upstream pagination redesign; the multi-volume book currently opens vol.1
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
	private const string KN_Apadana2Buddhavamsa_2010 = "022-Apadānapāḷi – 2 Buddhavaṃsa- Cariyāpiṭakapāḷi.pdf";
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
		addSource("vin02m3.mul.xml", SourceType.Burmese1957, 11, $"{Burmese1957Vinaya}/{Vin_Culavagga_1957}");
		addSource("vin02m4.mul.xml", SourceType.Burmese1957, 15, $"{Burmese1957Vinaya}/{Vin_Parivara_1957}");

		// --- Sutta Pitaka - Dīgha Nikāya ---
		addSource("s0101m.mul.xml", SourceType.Burmese1957, 19, $"{Burmese1957Sutta}/{DN_Silakkhandhavagga_1957}");
		addSource("s0102m.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Sutta}/{DN_Mahavagga_1957}");
		addSource("s0103m.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Sutta}/{DN_Pathikavagga_1957}");

		// --- Sutta Pitaka - Majjhima Nikāya ---
		addSource("s0201m.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{MN_Mulapannasa_1957}", new[] { 379 });
		addSource("s0202m.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Sutta}/{MN_Majjhimapannasa_1957}");
		addSource("s0203m.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Sutta}/{MN_Uparipannasa_1957}");

		// --- Sutta Pitaka - Saṃyutta Nikāya ---
		addSource("s0301m.mul.xml", SourceType.Burmese1957, 38, $"{Burmese1957Sutta}/{SN_SagathaNidana_1957}");
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
		addSource("s0504m.mul.xml", SourceType.Burmese1957, 23, $"{Burmese1957Sutta}/{KN_KhuddakaSuttanipata_1957}");
		addSource("s0505m.mul.xml", SourceType.Burmese1957, 117, $"{Burmese1957Sutta}/{KN_KhuddakaSuttanipata_1957}"); // PDF has 100 duplicate pages; startPage should be ~23 once fixed
		addSource("s0506m.mul.xml", SourceType.Burmese1957, 17, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0507m.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0508m.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0509m.mul.xml", SourceType.Burmese1957, 15, $"{Burmese1957Sutta}/{KN_VimanavattuTherigatha_1957}");
		addSource("s0510m1.mul.xml", SourceType.Burmese1957, 20, $"{Burmese1957Sutta}/{KN_Apadana1_1957}");
		addSource("s0510m2.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Sutta}/{KN_Apadana2Buddhavamsa_1957}", new[] { 186 });
		addSource("s0511m.mul.xml", SourceType.Burmese1957, 13, $"{Burmese1957Sutta}/{KN_Apadana2Buddhavamsa_1957}");
		addSource("s0512m.mul.xml", SourceType.Burmese1957, 13, $"{Burmese1957Sutta}/{KN_Apadana2Buddhavamsa_1957}");
		addSource("s0513m.mul.xml", SourceType.Burmese1957, 27, $"{Burmese1957Sutta}/{KN_Jataka1_1957}");
		addSource("s0514m.mul.xml", SourceType.Burmese1957, 5, $"{Burmese1957Sutta}/{KN_Jataka2_1957}");
		addSource("s0515m.mul.xml", SourceType.Burmese1957, 5, $"{Burmese1957Sutta}/{KN_Mahaniddesa_1957}");
		addSource("s0516m.mul.xml", SourceType.Burmese1957, 6, $"{Burmese1957Sutta}/{KN_Culaniddesa_1957}");
		addSource("s0517m.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Sutta}/{KN_Patisambhidamagga_1957}");
		addSource("s0519m.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Sutta}/{KN_NettiPetakopadesa_1957}");
		addSource("s0518m.nrf.xml", SourceType.Burmese1957, 15, $"{Burmese1957Sutta}/{KN_Milindapanha_1957}");
		addSource("s0520m.nrf.xml", SourceType.Burmese1957, 5, $"{Burmese1957Sutta}/{KN_NettiPetakopadesa_1957}");

		// --- Abhidhamma Pitaka ---
		addSource("abh01m.mul.xml", SourceType.Burmese1957, 1, $"{Burmese1957Abhidhamma}/{Abh_Dhammasangani_1957}"); // TODO: PDF is 0 bytes in SharePoint
		addSource("abh02m.mul.xml", SourceType.Burmese1957, 11, $"{Burmese1957Abhidhamma}/{Abh_Vibhanga_1957}");
		// abh03m1 = Dhatukatha, abh03m2 = Puggalapannatti (both in combined PDF)
		addSource("abh03m1.mul.xml", SourceType.Burmese1957, 8, $"{Burmese1957Abhidhamma}/{Abh_DhatukathaPuggalapannatti_1957}");
		addSource("abh03m2.mul.xml", SourceType.Burmese1957, 8, $"{Burmese1957Abhidhamma}/{Abh_DhatukathaPuggalapannatti_1957}");
		addSource("abh03m3.mul.xml", SourceType.Burmese1957, 16, $"{Burmese1957Abhidhamma}/{Abh_Kathavatthu_1957}");
		addSource("abh03m4.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Abhidhamma}/{Abh_Yamaka1_1957}", new[] { 16 });
		addSource("abh03m5.mul.xml", SourceType.Burmese1957, 12, $"{Burmese1957Abhidhamma}/{Abh_Yamaka2_1957}");
		addSource("abh03m6.mul.xml", SourceType.Burmese1957, 1, $"{Burmese1957Abhidhamma}/{Abh_Yamaka3_1957}"); // TODO: PDF is 0 bytes in SharePoint
		addSource("abh03m7.mul.xml", SourceType.Burmese1957, 30, $"{Burmese1957Abhidhamma}/{Abh_Patthana1_1957}");
		addSource("abh03m8.mul.xml", SourceType.Burmese1957, 28, $"{Burmese1957Abhidhamma}/{Abh_Patthana2_1957}");
		addSource("abh03m9.mul.xml", SourceType.Burmese1957, 7, $"{Burmese1957Abhidhamma}/{Abh_Patthana3_1957}");
		addSource("abh03m10.mul.xml", SourceType.Burmese1957, 10, $"{Burmese1957Abhidhamma}/{Abh_Patthana4_1957}", new[] { 466 });
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
		addSource("s0203m.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{MN_Uparipannasa_2010}");

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
		addSource("s0504m.mul.xml", SourceType.Burmese2010, 29, $"{Burmese2010Mula}/{KN_KhuddakaSuttanipata_2010}");
		addSource("s0505m.mul.xml", SourceType.Burmese2010, 27, $"{Burmese2010Mula}/{KN_KhuddakaSuttanipata_2010}");
		addSource("s0506m.mul.xml", SourceType.Burmese2010, 21, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}");
		addSource("s0507m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}");
		addSource("s0508m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}");
		addSource("s0509m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_VimanavattuTherigatha_2010}");
		addSource("s0510m1.mul.xml", SourceType.Burmese2010, 25, $"{Burmese2010Mula}/{KN_Apadana1_2010}");
		addSource("s0510m2.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Apadana2Buddhavamsa_2010}", new[] { 186 });
		addSource("s0511m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Apadana2Buddhavamsa_2010}", new[] { 186 });
		addSource("s0512m.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Apadana2Buddhavamsa_2010}", new[] { 186 });
		addSource("s0513m.mul.xml", SourceType.Burmese2010, 31, $"{Burmese2010Mula}/{KN_Jataka1_2010}");
		addSource("s0514m.mul.xml", SourceType.Burmese2010, 9, $"{Burmese2010Mula}/{KN_Jataka2_2010}");
		addSource("s0515m.mul.xml", SourceType.Burmese2010, 9, $"{Burmese2010Mula}/{KN_Mahaniddesa_2010}");
		addSource("s0516m.mul.xml", SourceType.Burmese2010, 9, $"{Burmese2010Mula}/{KN_Culaniddesa_2010}");
		addSource("s0517m.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{KN_Patisambhidamagga_2010}");
		addSource("s0519m.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{KN_NettiPetakopadesa_2010}");
		addSource("s0518m.nrf.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{KN_Milindapanha_2010}");
		addSource("s0520m.nrf.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{KN_NettiPetakopadesa_2010}");

		// --- Abhidhamma Pitaka ---
		addSource("abh01m.mul.xml", SourceType.Burmese2010, 23, $"{Burmese2010Mula}/{Abh_Dhammasangani_2010}");
		addSource("abh02m.mul.xml", SourceType.Burmese2010, 15, $"{Burmese2010Mula}/{Abh_Vibhanga_2010}");
		// abh03m1 = Dhatukatha, abh03m2 = Puggalapannatti (both in combined PDF)
		addSource("abh03m1.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{Abh_DhatukathaPuggalapannatti_2010}");
		addSource("abh03m2.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{Abh_DhatukathaPuggalapannatti_2010}");
		addSource("abh03m3.mul.xml", SourceType.Burmese2010, 19, $"{Burmese2010Mula}/{Abh_Kathavatthu_2010}");
		addSource("abh03m4.mul.xml", SourceType.Burmese2010, 15, $"{Burmese2010Mula}/{Abh_Yamaka1_2010}", new[] { 16 });
		addSource("abh03m5.mul.xml", SourceType.Burmese2010, 15, $"{Burmese2010Mula}/{Abh_Yamaka2_2010}");
		addSource("abh03m6.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{Abh_Yamaka3_2010}");
		addSource("abh03m7.mul.xml", SourceType.Burmese2010, 35, $"{Burmese2010Mula}/{Abh_Patthana1_2010}");
		addSource("abh03m8.mul.xml", SourceType.Burmese2010, 31, $"{Burmese2010Mula}/{Abh_Patthana2_2010}");
		addSource("abh03m9.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{Abh_Patthana3_2010}");
		addSource("abh03m10.mul.xml", SourceType.Burmese2010, 13, $"{Burmese2010Mula}/{Abh_Patthana4_2010}", new[] { 466 });
		addSource("abh03m11.mul.xml", SourceType.Burmese2010, 11, $"{Burmese2010Mula}/{Abh_Patthana5_2010}", new[] { 62, 122, 158, 196, 214, 306, 326, 348, 376, 402, 424 });

		// =============================================
		// 1957 Atthakatha (Commentary) Mappings
		// =============================================
		// Note: Only 1957 has Atthakatha PDFs available

		// --- Vinaya Atthakatha ---
		addSource("vin01a.att.xml", SourceType.Burmese1957, 15, $"{Burmese1957Atthakatha}/{Att_Vin_Parajika1_1957}");
		addSource("vin02a1.att.xml", SourceType.Burmese1957, 11, $"{Burmese1957Atthakatha}/{Att_Vin_Pacittiyadi_1957}");
		addSource("vin02a2.att.xml", SourceType.Burmese1957, 12, $"{Burmese1957Atthakatha}/{Att_Vin_Culavagga_1957}");
		addSource("vin02a3.att.xml", SourceType.Burmese1957, 12, $"{Burmese1957Atthakatha}/{Att_Vin_Culavagga_1957}");
		addSource("vin02a4.att.xml", SourceType.Burmese1957, 12, $"{Burmese1957Atthakatha}/{Att_Vin_Culavagga_1957}");

		// --- DN Atthakatha ---
		addSource("s0101a.att.xml", SourceType.Burmese1957, 16, $"{Burmese1957Atthakatha}/{Att_DN_Silakkhandhavagga_1957}");
		addSource("s0102a.att.xml", SourceType.Burmese1957, 9, $"{Burmese1957Atthakatha}/{Att_DN_Mahavagga_1957}");
		addSource("s0103a.att.xml", SourceType.Burmese1957, 9, $"{Burmese1957Atthakatha}/{Att_DN_Pathikavagga_1957}");

		// --- MN Atthakatha ---
		addSource("s0201a.att.xml", SourceType.Burmese1957, 16, $"{Burmese1957Atthakatha}/{Att_MN_Mulapannasa1_1957}");
		addSource("s0202a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_MN_Majjhimapannasa_1957}");
		addSource("s0203a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_MN_Uparipannasa_1957}");

		// --- SN Atthakatha ---
		addSource("s0301a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_SN_Sagathavagga_1957}");
		addSource("s0302a.att.xml", SourceType.Burmese1957, 18, $"{Burmese1957Atthakatha}/{Att_SN_NidanaKhandha_1957}");
		addSource("s0303a.att.xml", SourceType.Burmese1957, 17, $"{Burmese1957Atthakatha}/{Att_SN_NidanaKhandha_1957}");
		addSource("s0304a.att.xml", SourceType.Burmese1957, 23, $"{Burmese1957Atthakatha}/{Att_SN_SalayatanaMaha_1957}");
		addSource("s0305a.att.xml", SourceType.Burmese1957, 23, $"{Burmese1957Atthakatha}/{Att_SN_SalayatanaMaha_1957}");

		// --- AN Atthakatha ---
		addSource("s0401a.att.xml", SourceType.Burmese1957, 3, $"{Burmese1957Atthakatha}/{Att_AN_Ekakanipata_1957}");
		addSource("s0402a.att.xml", SourceType.Burmese1957, 19, $"{Burmese1957Atthakatha}/{Att_AN_DukaTikaCatukka_1957}", new[] { 68, 248 });
		addSource("s0403a.att.xml", SourceType.Burmese1957, 27, $"{Burmese1957Atthakatha}/{Att_AN_Pancakanipata_1957}", new[] { 86, 144 });
		addSource("s0404a.att.xml", SourceType.Burmese1957, 27, $"{Burmese1957Atthakatha}/{Att_AN_Pancakanipata_1957}", new[] { 86, 144, 256, 286, 342 });

		// --- KN Atthakatha ---
		addSource("s0501a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Khuddakapatha_1957}");
		addSource("s0502a.att.xml", SourceType.Burmese1957, 8, $"{Burmese1957Atthakatha}/{Att_KN_Dhammapada1_1957}");
		addSource("s0503a.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Udana_1957}");
		addSource("s0504a.att.xml", SourceType.Burmese1957, 8, $"{Burmese1957Atthakatha}/{Att_KN_Itivuttaka_1957}");
		addSource("s0505a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Suttanipata1_1957}");
		addSource("s0506a.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Vimanavatthu_1957}");
		addSource("s0507a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_KN_Petavatthu_1957}");
		addSource("s0508a1.att.xml", SourceType.Burmese1957, 11, $"{Burmese1957Atthakatha}/{Att_KN_Theragatha1_1957}");
		addSource("s0508a2.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Theragatha2_1957}", new[] { 360 });
		addSource("s0509a.att.xml", SourceType.Burmese1957, 7, $"{Burmese1957Atthakatha}/{Att_KN_Therigatha_1957}");
		addSource("s0510a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Apadana1_1957}");
		addSource("s0511a.att.xml", SourceType.Burmese1957, 5, $"{Burmese1957Atthakatha}/{Att_KN_Buddhavamsa_1957}");
		addSource("s0512a.att.xml", SourceType.Burmese1957, 5, $"{Burmese1957Atthakatha}/{Att_KN_Cariyapitaka_1957}");
		addSource("s0513a1.att.xml", SourceType.Burmese1957, 10, $"{Burmese1957Atthakatha}/{Att_KN_Jataka1_1957}");
		addSource("s0513a2.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Jataka2_1957}");
		addSource("s0513a3.att.xml", SourceType.Burmese1957, 10, $"{Burmese1957Atthakatha}/{Att_KN_Jataka3_1957}");
		addSource("s0513a4.att.xml", SourceType.Burmese1957, 8, $"{Burmese1957Atthakatha}/{Att_KN_Jataka4_1957}");
		addSource("s0514a1.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_KN_Jataka5_1957}");
		addSource("s0514a2.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Jataka6_1957}", new[] { 38, 118 });
		addSource("s0514a3.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Jataka7_1957}", new[] { 150 });
		addSource("s0515a.att.xml", SourceType.Burmese1957, 4, $"{Burmese1957Atthakatha}/{Att_KN_Mahaniddesa_1957}");
		addSource("s0516a.att.xml", SourceType.Burmese1957, 5, $"{Burmese1957Atthakatha}/{Att_KN_CulaniddesaNetti_1957}");
		addSource("s0517a.att.xml", SourceType.Burmese1957, 6, $"{Burmese1957Atthakatha}/{Att_KN_Patisambhida1_1957}");
		addSource("s0519a.att.xml", SourceType.Burmese1957, 5, $"{Burmese1957Atthakatha}/{Att_KN_CulaniddesaNetti_1957}");

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

		// --- Vinaya Tika (Saratthadipani) ---
		addSource("vin01t1.tik.xml", SourceType.Burmese1957, 14, $"{Burmese1957Tika}/{Tik_Vin_Saratthadipani1_1957}");
		addSource("vin01t2.tik.xml", SourceType.Burmese1957, 9, $"{Burmese1957Tika}/{Tik_Vin_Saratthadipani2_1957}");
		addSource("vin02t.tik.xml", SourceType.Burmese1957, 21, $"{Burmese1957Tika}/{Tik_Vin_Saratthadipani3_1957}", new[] { 130, 364 });

		// --- Abhidhamma Tika ---
		addSource("abh01t.tik.xml", SourceType.Burmese1957, 16, $"{Burmese1957Tika}/{Tik_Abh_Dhammasangani_1957}");
		addSource("abh02t.tik.xml", SourceType.Burmese1957, 9, $"{Burmese1957Tika}/{Tik_Abh_Vibhanga_1957}");
		addSource("abh03t.tik.xml", SourceType.Burmese1957, 20, $"{Burmese1957Tika}/{Tik_Abh_Pancappakarana_1957}", new[] { 46 });

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
		// PATHS are mapped here; page offsets are placeholder 1 pending per-PDF inspection (a separate step).
		// Books whose text has no source PDF in the Anya set are intentionally left unmapped (no button).

		// Saṅgāyana Pucchā-Vissajjanā. Multi-volume texts map to Vol 1 for now (Vol 2 → #76). e0907 has no PDF.
		addSource("e0901n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSangayana}/1. Dīghanikāya (Pu-Vi).pdf");
		addSource("e0902n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSangayana}/2. Majjhimanikāya (Pu-Vi)_Vol 1.pdf");
		addSource("e0903n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSangayana}/3. Saṃyuttanikāya (Pu-Vi)_Vol 1.pdf");
		addSource("e0904n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSangayana}/4. Aṅguttaranikāya (Pu-Vi)_Vol 1.pdf");
		addSource("e0905n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSangayana}/5. Vinayapiṭaka (Pu-Vi)_Vol 1.pdf");
		addSource("e0906n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSangayana}/6. Abhidhammapiṭaka (Pu-Vi).pdf");

		// Leḍī Sayāḍo. e0301 (Paramatthadīpanī) has no PDF.
		addSource("e0201n.nrf.xml", SourceType.BurmeseAnya, 112, $"{BurmeseAnyaLedi}/1. Niruttidīpanī.pdf");
		addSource("e0401n.nrf.xml", SourceType.BurmeseAnya, 8, $"{BurmeseAnyaLedi}/3. Anudīpanīpāṭha.pdf");
		addSource("e0501n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaLedi}/4. Paṭṭhānuddesa dīpanīpāṭha_printed.pdf");

		// Buddha-vandanā. e0603–e0608 have no PDF.
		addSource("e0601n.nrf.xml", SourceType.BurmeseAnya, 14, $"{BurmeseAnyaBuddhaVandana}/1. Namakkāraṭīkā.pdf");
		addSource("e0602n.nrf.xml", SourceType.BurmeseAnya, 4, $"{BurmeseAnyaBuddhaVandana}/2. Mahāpaṇāmapāṭha.pdf");

		// Vaṃsa. e0701 (Cūḷagandhavaṃsa) has no PDF. e0703 (Mahāvaṃsa) is multi-vol → Vol 1.
		addSource("e0702n.nrf.xml", SourceType.BurmeseAnya, 16, $"{BurmeseAnyaVamsa}/3. Sāsanavaṃsappadīpikā.pdf");
		addSource("e0703n.nrf.xml", SourceType.BurmeseAnya, 7, $"{BurmeseAnyaVamsa}/2. Mahāvaṃsa (Vol-1).pdf");

		// Byākaraṇa (grammar). e0808/e0809/e0811 share one combined PDF (offsets to distinguish → deferred).
		// e0812 (Subodhālaṅkāraṭīkā) has no PDF.
		addSource("e0801n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/2. Moggallānabyākaraṇaṃ.pdf");
		addSource("e0802n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/1. Kaccāyanabyākaraṇaṃ_1990.pdf");
		addSource("e0803n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/3. Saddanītippakaraṇaṃ (padamālā).pdf");
		addSource("e0804n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/4. Saddanītippakaraṇaṃ (dhātumālā).pdf");
		addSource("e0805n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/6. Padarūpasiddhi.pdf");
		addSource("e0806n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/7. Moggallānapañcikā.pdf");
		addSource("e0807n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/8. Payogasiddhipāṭha.pdf");
		addSource("e0808n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/9. Vuttodaya 10. Abhidhānappadīpikāpātha 12. Subodhālaṅkāro.pdf");
		addSource("e0809n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/9. Vuttodaya 10. Abhidhānappadīpikāpātha 12. Subodhālaṅkāro.pdf");
		addSource("e0810n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/11. Abhidhānappadīpikāṭīkā.pdf");
		addSource("e0811n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/9. Vuttodaya 10. Abhidhānappadīpikāpātha 12. Subodhālaṅkāro.pdf");
		addSource("e0813n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaByakarana}/14. Bālāvatāra gaṇṭhipadatthavinicchayasāra.pdf");

		// Nīti. e1002 (Nītimañjarī), e1005 (Lokanīti), e1006 (Suttantanīti) have no PDF.
		addSource("e1001n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaNiti}/6. Kavidappaṇanīti.pdf");
		addSource("e1003n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaNiti}/5. Dhammanīti.pdf");
		addSource("e1004n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaNiti}/4. Mahārahanīti.pdf");
		addSource("e1007n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaNiti}/3. Sūrassatinīti.pdf");
		addSource("e1008n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaNiti}/10. Cāṇakyanīti.pdf");
		addSource("e1009n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaNiti}/8. Naradakkhadīpanī.pdf");
		addSource("e1010n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaNiti}/9. Caturārakkhadīpanī and Kāyapaccavekkhana.pdf");

		// Pakiṇṇaka. e1102 (Sīmavisodhanī) has no PDF.
		addSource("e1101n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaPakinnaka}/1. Rasavāhinī.pdf");
		addSource("e1103n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaPakinnaka}/3. Vessantaragīti.pdf");

		// Sīhaḷa. e1203 (Dāṭhāvaṃsa) + e1205 (Dhātuvaṃsa) share one combined PDF. The other 11 texts have no PDF.
		addSource("e1201n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSihala}/1. Moggallāna vuttivivaraṇapañcikā (with Moggallānabyākaraṇaṃ).pdf");
		addSource("e1203n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSihala}/3. and 5. Dāṭhāvaṃsa_Dhātuvaṃsa.pdf");
		addSource("e1205n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSihala}/3. and 5. Dāṭhāvaṃsa_Dhātuvaṃsa.pdf");
		addSource("e1209n.nrf.xml", SourceType.BurmeseAnya, 1, $"{BurmeseAnyaSihala}/9. Telakaṭāhagāthā Pali Burmese English.pdf");
	}

	private void addSource(string filename, SourceType sourceType, int pageStart, string path,
		int[]? missingPages = null)
	{
		if (!sourcesMap.TryGetValue(filename, out var d))
		{
			d = new Dictionary<SourceType, Source>();
			sourcesMap[filename] = d;
		}
		d[sourceType] = new Source(sourceType, pageStart, path, missingPages);
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
		public Source(SourceType sourceType, int pageStart, string path, int[]? missingPages = null)
		{
			SourceType = sourceType;
			PageStart = pageStart;
			Path = path;
			MissingPages = missingPages ?? Array.Empty<int>();
		}

		/// <summary>
		/// Relative path within SharePoint's _Source folder.
		/// Use with ISharePointService.DownloadPdfAsync().
		/// </summary>
		public string Path { get; set; }

		public SourceType SourceType { get; set; }
		public int PageStart { get; set; }

		/// <summary>
		/// Print page numbers that exist in the printed volume but have NO page in this PDF — blank
		/// pages the scanner passed over. Ascending. (#540)
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
		/// PDF page for a printed page number, accounting for pages absent from the scan.
		/// </summary>
		public int PdfPageFor(int printPage)
		{
			int skipped = 0;
			foreach (int p in MissingPages)
			{
				if (p >= printPage) break;   // MissingPages is ascending
				skipped++;
			}
			int pdfPage = PageStart + (printPage - 1) - skipped;
			return pdfPage < 1 ? 1 : pdfPage;
		}
	}
}
