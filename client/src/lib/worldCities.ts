/// <reference types="vite/client" />
/**
 * The cities a member can pick for the globe: every country's capital and its larger cities,
 * with coordinates, so a chosen name becomes a pin without ever asking the browser where the
 * person is. One line per country — ISO 3166-1 alpha-2 code, then "City lat lon" entries.
 * The flag for each code ships in /flags (from the MIT-licensed flag-icons set), so it works
 * offline in the phone app too.
 */
const RAW = `
AF Kabul 34.53 69.17; Herat 34.35 62.20; Kandahar 31.61 65.71; Mazar-i-Sharif 36.71 67.11
AL Tirana 41.33 19.82; Durrës 41.32 19.45; Vlorë 40.47 19.49
DZ Algiers 36.75 3.06; Oran 35.70 -0.63; Constantine 36.37 6.61
AD Andorra la Vella 42.51 1.52
AO Luanda -8.84 13.23; Huambo -12.78 15.74; Lobito -12.36 13.54
AG Saint John's 17.12 -61.85
AR Buenos Aires -34.60 -58.38; Córdoba -31.42 -64.18; Rosario -32.95 -60.64; Mendoza -32.89 -68.83; Mar del Plata -38.00 -57.56
AM Yerevan 40.18 44.51; Gyumri 40.79 43.85
AU Sydney -33.87 151.21; Melbourne -37.81 144.96; Brisbane -27.47 153.03; Perth -31.95 115.86; Adelaide -34.93 138.60; Canberra -35.28 149.13; Gold Coast -28.02 153.40; Hobart -42.88 147.33; Darwin -12.46 130.84
AT Vienna 48.21 16.37; Graz 47.07 15.44; Linz 48.31 14.29; Salzburg 47.81 13.06; Innsbruck 47.27 11.40
AZ Baku 40.41 49.87; Ganja 40.68 46.36; Sumqayit 40.59 49.63; Mingachevir 40.76 47.06; Lankaran 38.75 48.85; Shaki 41.19 47.17; Nakhchivan 39.21 45.41; Shirvan 39.93 48.93; Quba 41.36 48.51; Shusha 39.76 46.75; Khankendi 39.82 46.75; Gabala 40.98 47.85; Yevlakh 40.62 47.15; Barda 40.37 47.13; Zaqatala 41.63 46.64; Masalli 39.03 48.67; Agdam 39.99 46.93; Fuzuli 39.60 47.15; Shamakhi 40.63 48.64; Ismayilli 40.79 48.15; Goychay 40.65 47.74; Agdash 40.65 47.48; Salyan 39.60 48.98; Sabirabad 40.01 48.48; Tovuz 40.99 45.63; Qazax 41.09 45.37; Astara 38.46 48.87; Lerik 38.77 48.42; Siyazan 41.08 49.11; Khachmaz 41.46 48.81; Naftalan 40.51 46.82; Kalbajar 40.10 46.04; Lachin 39.64 46.55; Jabrayil 39.40 47.03; Zangilan 39.09 46.65; Gadabay 40.57 45.81; Shabran 41.22 48.99
BS Nassau 25.05 -77.35
BH Manama 26.23 50.59
BD Dhaka 23.81 90.41; Chittagong 22.36 91.78; Khulna 22.85 89.54; Sylhet 24.89 91.87
BB Bridgetown 13.10 -59.61
BY Minsk 53.90 27.56; Gomel 52.44 30.98; Brest 52.10 23.69; Vitebsk 55.19 30.20
BE Brussels 50.85 4.35; Antwerp 51.22 4.40; Ghent 51.05 3.72; Liège 50.63 5.57; Bruges 51.21 3.22
BZ Belmopan 17.25 -88.77; Belize City 17.50 -88.20
BJ Porto-Novo 6.50 2.60; Cotonou 6.37 2.39
BT Thimphu 27.47 89.64
BO La Paz -16.49 -68.12; Santa Cruz de la Sierra -17.78 -63.18; Cochabamba -17.41 -66.16; Sucre -19.03 -65.26
BA Sarajevo 43.86 18.41; Banja Luka 44.77 17.19; Mostar 43.34 17.81
BW Gaborone -24.63 25.92; Francistown -21.17 27.51
BR São Paulo -23.55 -46.63; Rio de Janeiro -22.91 -43.17; Brasília -15.79 -47.88; Salvador -12.97 -38.50; Fortaleza -3.73 -38.53; Belo Horizonte -19.92 -43.94; Manaus -3.12 -60.02; Curitiba -25.43 -49.27; Recife -8.05 -34.88; Porto Alegre -30.03 -51.23
BN Bandar Seri Begawan 4.90 114.94
BG Sofia 42.70 23.32; Plovdiv 42.14 24.75; Varna 43.21 27.91; Burgas 42.50 27.47
BF Ouagadougou 12.37 -1.52; Bobo-Dioulasso 11.18 -4.30
BI Gitega -3.43 29.92; Bujumbura -3.36 29.36
CV Praia 14.93 -23.51
KH Phnom Penh 11.56 104.93; Siem Reap 13.36 103.86
CM Yaoundé 3.85 11.50; Douala 4.05 9.77
CA Toronto 43.65 -79.38; Montreal 45.50 -73.57; Vancouver 49.28 -123.12; Calgary 51.05 -114.07; Ottawa 45.42 -75.70; Edmonton 53.55 -113.49; Winnipeg 49.90 -97.14; Quebec City 46.81 -71.21; Halifax 44.65 -63.58
CF Bangui 4.39 18.56
TD N'Djamena 12.13 15.06
CL Santiago -33.45 -70.67; Valparaíso -33.05 -71.62; Concepción -36.83 -73.05; Antofagasta -23.65 -70.40
CN Beijing 39.90 116.41; Shanghai 31.23 121.47; Guangzhou 23.13 113.26; Shenzhen 22.54 114.06; Chengdu 30.57 104.07; Chongqing 29.56 106.55; Wuhan 30.59 114.31; Xi'an 34.34 108.94; Hangzhou 30.27 120.16; Nanjing 32.06 118.80; Tianjin 39.34 117.36; Harbin 45.80 126.53
CO Bogotá 4.71 -74.07; Medellín 6.24 -75.58; Cali 3.45 -76.53; Barranquilla 10.97 -74.80; Cartagena 10.39 -75.48
KM Moroni -11.70 43.26
CG Brazzaville -4.26 15.24; Pointe-Noire -4.78 11.86
CD Kinshasa -4.44 15.27; Lubumbashi -11.66 27.48; Goma -1.68 29.22
CR San José 9.93 -84.08
CI Yamoussoukro 6.83 -5.29; Abidjan 5.36 -4.01
HR Zagreb 45.81 15.98; Split 43.51 16.44; Rijeka 45.33 14.44; Dubrovnik 42.65 18.09
CU Havana 23.11 -82.37; Santiago de Cuba 20.02 -75.82
CY Nicosia 35.19 33.38; Limassol 34.71 33.02; Larnaca 34.92 33.63
CZ Prague 50.08 14.44; Brno 49.20 16.61; Ostrava 49.82 18.26
DK Copenhagen 55.68 12.57; Aarhus 56.16 10.20; Odense 55.40 10.39
DJ Djibouti 11.59 43.15
DM Roseau 15.30 -61.39
DO Santo Domingo 18.49 -69.93; Santiago de los Caballeros 19.45 -70.70; Punta Cana 18.58 -68.40
EC Quito -0.18 -78.47; Guayaquil -2.17 -79.92; Cuenca -2.90 -79.00
EG Cairo 30.04 31.24; Alexandria 31.20 29.92; Giza 30.01 31.21; Luxor 25.69 32.64; Sharm El Sheikh 27.92 34.33; Hurghada 27.26 33.81
SV San Salvador 13.69 -89.22
GQ Malabo 3.75 8.78
ER Asmara 15.32 38.93
EE Tallinn 59.44 24.75; Tartu 58.38 26.72
SZ Mbabane -26.31 31.14
ET Addis Ababa 9.03 38.74; Dire Dawa 9.59 41.87
FJ Suva -18.14 178.44
FI Helsinki 60.17 24.94; Espoo 60.21 24.66; Tampere 61.50 23.76; Turku 60.45 22.27; Oulu 65.01 25.47
FR Paris 48.86 2.35; Marseille 43.30 5.37; Lyon 45.76 4.84; Toulouse 43.60 1.44; Nice 43.70 7.27; Nantes 47.22 -1.55; Strasbourg 48.57 7.75; Bordeaux 44.84 -0.58; Lille 50.63 3.06; Montpellier 43.61 3.88; Cannes 43.55 7.02
GA Libreville 0.42 9.47
GM Banjul 13.45 -16.58
GE Tbilisi 41.72 44.83; Batumi 41.62 41.64; Kutaisi 42.27 42.70; Rustavi 41.55 45.00
DE Berlin 52.52 13.40; Hamburg 53.55 9.99; Munich 48.14 11.58; Cologne 50.94 6.96; Frankfurt 50.11 8.68; Stuttgart 48.78 9.18; Düsseldorf 51.23 6.77; Leipzig 51.34 12.37; Dresden 51.05 13.74; Hanover 52.38 9.73; Nuremberg 49.45 11.08; Bremen 53.08 8.80
GH Accra 5.60 -0.19; Kumasi 6.69 -1.62
GR Athens 37.98 23.73; Thessaloniki 40.64 22.94; Patras 38.25 21.73; Heraklion 35.34 25.14
GD Saint George's 12.06 -61.75
GT Guatemala City 14.63 -90.51; Antigua Guatemala 14.56 -90.73
GN Conakry 9.64 -13.58
GW Bissau 11.86 -15.60
GY Georgetown 6.80 -58.16
HT Port-au-Prince 18.59 -72.31
HN Tegucigalpa 14.07 -87.19; San Pedro Sula 15.50 -88.03
HU Budapest 47.50 19.04; Debrecen 47.53 21.63; Szeged 46.25 20.14
IS Reykjavík 64.15 -21.94
IN Delhi 28.61 77.21; Mumbai 19.08 72.88; Bangalore 12.97 77.59; Kolkata 22.57 88.36; Chennai 13.08 80.27; Hyderabad 17.39 78.49; Ahmedabad 23.02 72.57; Pune 18.52 73.86; Jaipur 26.91 75.79; Goa 15.50 73.83
ID Jakarta -6.21 106.85; Surabaya -7.25 112.75; Bandung -6.92 107.61; Medan 3.60 98.67; Denpasar -8.65 115.22; Yogyakarta -7.80 110.36
IR Tehran 35.69 51.39; Mashhad 36.30 59.60; Isfahan 32.65 51.67; Tabriz 38.08 46.29; Shiraz 29.59 52.58; Ardabil 38.25 48.29
IQ Baghdad 33.31 44.36; Basra 30.51 47.78; Erbil 36.19 44.01; Mosul 36.34 43.13
IE Dublin 53.35 -6.26; Cork 51.90 -8.47; Galway 53.27 -9.05
IL Jerusalem 31.77 35.21; Tel Aviv 32.09 34.78; Haifa 32.79 34.99
IT Rome 41.90 12.50; Milan 45.46 9.19; Naples 40.85 14.27; Turin 45.07 7.69; Palermo 38.12 13.36; Florence 43.77 11.26; Bologna 44.49 11.34; Venice 45.44 12.32; Genoa 44.41 8.93; Verona 45.44 10.99
JM Kingston 18.02 -76.80; Montego Bay 18.47 -77.92
JP Tokyo 35.68 139.65; Osaka 34.69 135.50; Yokohama 35.44 139.64; Nagoya 35.18 136.91; Sapporo 43.06 141.35; Fukuoka 33.59 130.40; Kyoto 35.01 135.77; Kobe 34.69 135.20; Hiroshima 34.39 132.46
JO Amman 31.95 35.93; Aqaba 29.53 35.01; Irbid 32.56 35.85
KZ Astana 51.17 71.45; Almaty 43.24 76.89; Shymkent 42.32 69.60; Aktau 43.65 51.17; Atyrau 47.09 51.92; Karaganda 49.81 73.09
KE Nairobi -1.29 36.82; Mombasa -4.04 39.67; Kisumu -0.09 34.77
KI Tarawa 1.45 173.03
KP Pyongyang 39.04 125.76
KR Seoul 37.57 126.98; Busan 35.18 129.08; Incheon 37.46 126.71; Daegu 35.87 128.60; Daejeon 36.35 127.38; Gwangju 35.16 126.85
XK Pristina 42.66 21.17; Prizren 42.21 20.74
KW Kuwait City 29.38 47.99
KG Bishkek 42.87 74.59; Osh 40.53 72.80
LA Vientiane 17.98 102.63; Luang Prabang 19.89 102.13
LV Riga 56.95 24.11; Daugavpils 55.87 26.54
LB Beirut 33.89 35.50; Tripoli 34.44 35.83
LS Maseru -29.31 27.48
LR Monrovia 6.30 -10.80
LY Tripoli 32.89 13.19; Benghazi 32.12 20.09
LI Vaduz 47.14 9.52
LT Vilnius 54.69 25.28; Kaunas 54.90 23.90; Klaipėda 55.70 21.14
LU Luxembourg 49.61 6.13
MG Antananarivo -18.88 47.51
MW Lilongwe -13.96 33.79; Blantyre -15.79 35.01
MY Kuala Lumpur 3.14 101.69; George Town 5.41 100.34; Johor Bahru 1.49 103.74; Kota Kinabalu 5.98 116.07
MV Malé 4.18 73.51
ML Bamako 12.64 -8.00
MT Valletta 35.90 14.51
MH Majuro 7.09 171.38
MR Nouakchott 18.08 -15.98
MU Port Louis -20.16 57.50
MX Mexico City 19.43 -99.13; Guadalajara 20.66 -103.35; Monterrey 25.69 -100.32; Puebla 19.04 -98.21; Tijuana 32.51 -117.04; Cancún 21.16 -86.85; Mérida 20.97 -89.62
FM Palikir 6.92 158.16
MD Chișinău 47.01 28.86; Bălți 47.76 27.93
MC Monaco 43.74 7.42
MN Ulaanbaatar 47.89 106.91
ME Podgorica 42.43 19.26; Budva 42.29 18.84
MA Rabat 34.02 -6.84; Casablanca 33.57 -7.59; Marrakesh 31.63 -7.99; Fez 34.03 -5.00; Tangier 35.76 -5.83; Agadir 30.43 -9.60
MZ Maputo -25.97 32.57; Beira -19.84 34.84
MM Naypyidaw 19.76 96.08; Yangon 16.84 96.17; Mandalay 21.96 96.09
NA Windhoek -22.56 17.08
NR Yaren -0.55 166.92
NP Kathmandu 27.72 85.32; Pokhara 28.21 83.99
NL Amsterdam 52.37 4.90; Rotterdam 51.92 4.48; The Hague 52.07 4.30; Utrecht 52.09 5.12; Eindhoven 51.44 5.47
NZ Wellington -41.29 174.78; Auckland -36.85 174.76; Christchurch -43.53 172.64; Queenstown -45.03 168.66
NI Managua 12.11 -86.24
NE Niamey 13.51 2.11
NG Abuja 9.08 7.40; Lagos 6.52 3.38; Kano 12.00 8.52; Ibadan 7.38 3.95; Port Harcourt 4.82 7.05
MK Skopje 42.00 21.43; Ohrid 41.12 20.80
NO Oslo 59.91 10.75; Bergen 60.39 5.32; Trondheim 63.43 10.40; Stavanger 58.97 5.73; Tromsø 69.65 18.96
OM Muscat 23.59 58.41; Salalah 17.02 54.09
PK Islamabad 33.68 73.05; Karachi 24.86 67.01; Lahore 31.55 74.34; Faisalabad 31.45 73.14; Peshawar 34.01 71.58
PW Ngerulmud 7.50 134.62
PS Ramallah 31.90 35.20; Gaza 31.50 34.47
PA Panama City 8.98 -79.52
PG Port Moresby -9.44 147.18
PY Asunción -25.26 -57.58; Ciudad del Este -25.51 -54.61
PE Lima -12.05 -77.04; Arequipa -16.41 -71.54; Cusco -13.53 -71.97; Trujillo -8.11 -79.03
PH Manila 14.60 120.98; Quezon City 14.68 121.04; Cebu City 10.32 123.89; Davao City 7.19 125.46
PL Warsaw 52.23 21.01; Kraków 50.06 19.94; Łódź 51.76 19.46; Wrocław 51.11 17.04; Poznań 52.41 16.93; Gdańsk 54.35 18.65; Szczecin 53.43 14.55
PT Lisbon 38.72 -9.14; Porto 41.15 -8.61; Braga 41.55 -8.43; Faro 37.02 -7.93; Funchal 32.65 -16.91
QA Doha 25.29 51.53
RO Bucharest 44.43 26.10; Cluj-Napoca 46.77 23.60; Timișoara 45.75 21.23; Iași 47.16 27.59; Constanța 44.18 28.63; Brașov 45.66 25.61
RU Moscow 55.76 37.62; Saint Petersburg 59.93 30.36; Novosibirsk 55.01 82.93; Yekaterinburg 56.84 60.61; Kazan 55.80 49.11; Nizhny Novgorod 56.30 43.94; Samara 53.20 50.15; Rostov-on-Don 47.24 39.71; Krasnodar 45.04 38.98; Sochi 43.60 39.73; Vladivostok 43.12 131.89; Derbent 42.06 48.29; Makhachkala 42.98 47.50; Omsk 54.99 73.37; Ufa 54.74 55.97
RW Kigali -1.94 30.06
KN Basseterre 17.30 -62.72
LC Castries 14.01 -60.99
VC Kingstown 13.16 -61.22
WS Apia -13.83 -171.76
SM San Marino 43.94 12.45
ST São Tomé 0.34 6.73
SA Riyadh 24.71 46.68; Jeddah 21.49 39.19; Mecca 21.39 39.86; Medina 24.47 39.61; Dammam 26.43 50.10
SN Dakar 14.72 -17.47; Touba 14.85 -15.88
RS Belgrade 44.79 20.45; Novi Sad 45.27 19.83; Niš 43.32 21.90
SC Victoria -4.62 55.45
SL Freetown 8.47 -13.23
SG Singapore 1.35 103.82
SK Bratislava 48.15 17.11; Košice 48.72 21.26
SI Ljubljana 46.06 14.51; Maribor 46.55 15.65
SB Honiara -9.43 159.96
SO Mogadishu 2.05 45.32; Hargeisa 9.56 44.06
ZA Johannesburg -26.20 28.05; Cape Town -33.92 18.42; Durban -29.86 31.03; Pretoria -25.75 28.19; Port Elizabeth -33.96 25.60
SS Juba 4.85 31.58
ES Madrid 40.42 -3.70; Barcelona 41.39 2.17; Valencia 39.47 -0.38; Seville 37.39 -5.98; Zaragoza 41.65 -0.89; Málaga 36.72 -4.42; Bilbao 43.26 -2.93; Palma 39.57 2.65; Las Palmas 28.12 -15.44; Granada 37.18 -3.60
LK Sri Jayawardenepura Kotte 6.89 79.90; Colombo 6.93 79.86; Kandy 7.29 80.63
SD Khartoum 15.50 32.56; Omdurman 15.64 32.48; Port Sudan 19.62 37.22
SR Paramaribo 5.85 -55.20
SE Stockholm 59.33 18.07; Gothenburg 57.71 11.97; Malmö 55.60 13.00; Uppsala 59.86 17.64
CH Bern 46.95 7.45; Zürich 47.38 8.54; Geneva 46.20 6.14; Basel 47.56 7.59; Lausanne 46.52 6.63; Lucerne 47.05 8.31
SY Damascus 33.51 36.28; Aleppo 36.20 37.13; Latakia 35.52 35.78
TW Taipei 25.03 121.57; Kaohsiung 22.63 120.30; Taichung 24.15 120.67
TJ Dushanbe 38.56 68.77; Khujand 40.28 69.62
TZ Dodoma -6.16 35.75; Dar es Salaam -6.79 39.21; Arusha -3.39 36.68; Zanzibar -6.17 39.20
TH Bangkok 13.76 100.50; Chiang Mai 18.79 98.98; Phuket 7.88 98.39; Pattaya 12.93 100.88
TL Dili -8.56 125.56
TG Lomé 6.13 1.22
TO Nukuʻalofa -21.14 -175.20
TT Port of Spain 10.66 -61.51
TN Tunis 36.81 10.18; Sfax 34.74 10.76; Sousse 35.83 10.64
TR Istanbul 41.01 28.98; Ankara 39.93 32.86; Izmir 38.42 27.14; Bursa 40.19 29.06; Antalya 36.90 30.71; Adana 37.00 35.32; Konya 37.87 32.48; Gaziantep 37.07 37.38; Kayseri 38.73 35.49; Trabzon 41.00 39.72; Eskişehir 39.78 30.52; Samsun 41.29 36.33; Kars 40.60 43.10; Iğdır 39.92 44.04; Erzurum 39.90 41.27; Bodrum 37.03 27.43
TM Ashgabat 37.96 58.33; Türkmenbaşy 40.02 52.97; Mary 37.59 61.83
TV Funafuti -8.52 179.20
UG Kampala 0.35 32.58; Entebbe 0.05 32.46
UA Kyiv 50.45 30.52; Kharkiv 49.99 36.23; Odesa 46.48 30.72; Dnipro 48.46 35.05; Lviv 49.84 24.03; Zaporizhzhia 47.84 35.14
AE Abu Dhabi 24.45 54.38; Dubai 25.20 55.27; Sharjah 25.35 55.42; Al Ain 24.21 55.74; Ras Al Khaimah 25.80 55.98
GB London 51.51 -0.13; Manchester 53.48 -2.24; Birmingham 52.49 -1.89; Liverpool 53.41 -2.98; Leeds 53.80 -1.55; Glasgow 55.86 -4.25; Edinburgh 55.95 -3.19; Bristol 51.45 -2.59; Cardiff 51.48 -3.18; Belfast 54.60 -5.93; Newcastle 54.98 -1.62; Oxford 51.75 -1.26; Cambridge 52.21 0.12
US New York 40.71 -74.01; Los Angeles 34.05 -118.24; Chicago 41.88 -87.63; Houston 29.76 -95.37; Phoenix 33.45 -112.07; Philadelphia 39.95 -75.17; San Antonio 29.42 -98.49; San Diego 32.72 -117.16; Dallas 32.78 -96.80; San Francisco 37.77 -122.42; Seattle 47.61 -122.33; Boston 42.36 -71.06; Washington 38.91 -77.04; Miami 25.76 -80.19; Atlanta 33.75 -84.39; Las Vegas 36.17 -115.14; Denver 39.74 -104.99; Austin 30.27 -97.74; Nashville 36.16 -86.78; Portland 45.52 -122.68; Detroit 42.33 -83.05; Orlando 28.54 -81.38; New Orleans 29.95 -90.07; Honolulu 21.31 -157.86; Anchorage 61.22 -149.90
UY Montevideo -34.90 -56.16; Punta del Este -34.96 -54.95
UZ Tashkent 41.30 69.24; Samarkand 39.65 66.96; Bukhara 39.77 64.42; Namangan 41.00 71.67; Andijan 40.78 72.34; Khiva 41.38 60.36
VU Port Vila -17.73 168.32
VA Vatican City 41.90 12.45
VE Caracas 10.48 -66.90; Maracaibo 10.65 -71.64; Valencia 10.16 -68.00
VN Hanoi 21.03 105.85; Ho Chi Minh City 10.82 106.63; Da Nang 16.05 108.22; Haiphong 20.84 106.69
YE Sanaa 15.37 44.19; Aden 12.79 45.04
ZM Lusaka -15.39 28.32; Ndola -12.97 28.64
ZW Harare -17.83 31.05; Bulawayo -20.15 28.58
HK Hong Kong 22.32 114.17
MO Macau 22.20 113.54
PR San Juan 18.47 -66.11
GL Nuuk 64.18 -51.72
AX Mariehamn 60.10 19.94
AS Pago Pago -14.28 -170.70
AI The Valley 18.22 -63.06
AW Oranjestad 12.52 -70.03
BM Hamilton 32.29 -64.78
BQ Kralendijk 12.15 -68.27
IO Diego Garcia -7.31 72.41
KY George Town 19.29 -81.37
CX Flying Fish Cove -10.42 105.68
CC West Island -12.19 96.83
CK Avarua -21.21 -159.78
CW Willemstad 12.11 -68.93
FK Stanley -51.70 -57.85
FO Tórshavn 62.01 -6.77
GF Cayenne 4.92 -52.33
PF Papeete -17.54 -149.57
TF Port-aux-Français -49.35 70.22
GI Gibraltar 36.14 -5.35
GP Basse-Terre 16.00 -61.73; Pointe-à-Pitre 16.24 -61.53
GU Hagåtña 13.48 144.75
GG Saint Peter Port 49.46 -2.54
IM Douglas 54.15 -4.48
JE Saint Helier 49.19 -2.11
MQ Fort-de-France 14.62 -61.06
YT Mamoudzou -12.78 45.23
MS Brades 16.79 -62.21
NC Nouméa -22.28 166.46
NU Alofi -19.06 -169.92
NF Kingston -29.06 167.96
MP Saipan 15.18 145.75
PN Adamstown -25.07 -130.10
RE Saint-Denis -20.88 55.45
BL Gustavia 17.90 -62.85
SH Jamestown -15.93 -5.72
MF Marigot 18.07 -63.08
PM Saint-Pierre 46.78 -56.18
SX Philipsburg 18.03 -63.05
GS King Edward Point -54.28 -36.50
SJ Longyearbyen 78.22 15.65
TK Nukunonu -9.20 -171.85
TC Cockburn Town 21.46 -71.14
VG Road Town 18.43 -64.62
VI Charlotte Amalie 18.34 -64.93
WF Mata-Utu -13.28 -176.17
EH Laayoune 27.15 -13.20
AQ McMurdo Station -77.85 166.67
UM Wake Island 19.28 166.65
`;

export interface WorldCity {
  name: string;
  country: string;   // ISO 3166-1 alpha-2, upper case
  lat: number;
  lon: number;
}

export const WORLD_CITIES: WorldCity[] = RAW.trim().split("\n").flatMap((line) => {
  const country = line.slice(0, 2);
  return line.slice(3).split(";").map((entry) => {
    const parts = entry.trim().split(" ");
    const lon = Number(parts.pop());
    const lat = Number(parts.pop());
    return { name: parts.join(" "), country, lat, lon };
  });
});

/** Flag image for a country code, shipped with the app (works offline). */
export const flagUrl = (country: string) => `${import.meta.env.BASE_URL}flags/${country.toLowerCase()}.svg`;

/** The country's name in the page's language, e.g. "Azərbaycan" for AZ in Azerbaijani. */
export function countryName(country: string, locale: string): string {
  try {
    return new Intl.DisplayNames([locale, "en"], { type: "region" }).of(country) ?? country;
  } catch {
    return country;
  }
}

/** Case- and accent-insensitive key, so "baki", "Bakú" and "BAKU" meet. */
export const cityKey = (text: string) =>
  text.normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/ı/g, "i").toLowerCase().trim();
