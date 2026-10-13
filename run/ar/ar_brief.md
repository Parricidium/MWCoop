# Arabic translation brief — MWCoop launcher (My Winter Car co-op mod)

MWCoop is a co-op multiplayer mod for the Finnish survival and car game **My Winter Car** (sequel of My Summer Car, set in 1990s rural Finland). You translate the **launcher** (MWCoop.exe), a Windows app used to:
- host or join a game, through a lobby;
- customise your outfit and car;
- set graphics options;
- share mods;
- set up the Steam or IP network;
- send logs.

## Input
Each line: `ID <TAB> ENGLISH`. The English is written **as it appears in C++ source code**: `\n`, `\"`, `\\`, `é`-style escapes are literal backslash sequences.

## Output
A UTF-8 file, one line per translated entry: `ID <TAB> ARABIC`. Nothing else: no header, no quotes.
- **Escaping:** write the Arabic in the same source-code escaping.
  - A line break is the two characters `\n`, a double quote is `\"`, a backslash is `\\`.
  - Never put a raw `"` or a real tab or newline inside a translation.
  - Write Arabic letters directly in UTF-8. Do not add bidi control characters; the program adds them.
- **Placeholders:** keep every format placeholder exactly, in the same order: `%d`, `%s`, `%ls`, `%.1f`, `%02d`, `%%`, `{0}`, `{1}`… Keep every `\n` (same count).
- **Spacing:** keep leading and trailing spaces of the English.
- **Skip** (write no line for) entries that must stay as they are:
  - proper names: people, places, Steam, Discord, MSCLoader, Nexus, Radmin VPN, CORRIS, SATSUMA, SORBET, Kiitos…;
  - licences, dates, file names;
  - codes or units on their own (`30 m`, `x64`);
  - technical identifiers;
  - lines that are only a key name or a number;
  - French text that ended up in the English column.
- Artwork titles in the credits **should** be translated.

## Style
- Modern Standard Arabic, simple and friendly; address the player in the 2nd person masculine singular (the French original says "tu").
- UI space is limited: keep it **short**, close to the English length. Prefer short verbs for buttons ("ابدأ", "انضم", "استضف").
- ALL CAPS English buttons (HOST, JOIN, PLAY, START, READY, UPDATE…): plain Arabic. Arabic has no capitals; you may keep them short and strong.
- Keys stay in Latin: F, R, M, F5, F7, F8, F10, Esc, Enter, Tab, Shift, Ctrl, Q/E, Z, T…
  - "Left click" → "النقر الأيسر", "Right click" → "النقر الأيمن".
  - "Hold F" → "اضغط مطولاً على F".
- Use Western digits (0-9), not Arabic-Indic digits, because numbers often come from placeholders.
- Money "mk" (markka) stays "mk".

## Glossary (use consistently)
| English | Arabic |
|---|---|
| co-op | اللعب الجماعي (adjective: تعاوني) |
| host (player who hosts) | المضيف |
| HOST (button) | استضافة |
| JOIN (button) | انضمام |
| PLAY SOLO | العب وحدك |
| PLAY | العب |
| READY | جاهز |
| START (launch the game for everyone) | ابدأ |
| guest | الضيف |
| lobby | الردهة |
| launcher | المشغّل |
| save / game save | الحفظ / ملف الحفظ |
| mod / mods | تعديل / تعديلات (المودات is acceptable in headings) |
| nickname | الاسم المستعار |
| outfit | الملابس (an outfit = طقم) |
| appearance | المظهر |
| top / pants | القميص / البنطال |
| voice chat / push-to-talk | الدردشة الصوتية / زر التحدث |
| wallet / cash | المحفظة / النقود |
| cheats | الغشّ |
| map | الخريطة |
| drone | الطائرة المسيّرة |
| shop | المتجر |
| tape (VHS) / VCR / camcorder | شريط / جهاز الفيديو / كاميرا الفيديو |
| editing (video) / export | المونتاج / تصدير |
| firewall / router / port forwarding | جدار الحماية / الراوتر / تحويل المنفذ |
| invite | دعوة |
| settings | الإعدادات |
| graphics / shadows / reflections | الرسوميات / الظلال / الانعكاسات |
| ray tracing | تتبع الأشعة |
| anti-aliasing | تنعيم الحواف |
| draw distance / field of view | مسافة الرؤية / مجال الرؤية |
| logs | السجلات |
| update | تحديث |
| rally | الرالي |
| passenger / driver | الراكب / السائق |
| Off / On | إيقاف / تشغيل |
| Game (= game default) | اللعبة |
| Custom | مخصّص |
| Guide | الدليل |
| Credits | الشكر والتقدير |
| What's new | ما الجديد |
