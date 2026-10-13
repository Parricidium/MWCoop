# Russian translation brief — MWCoop (My Winter Car co-op mod)

MWCoop is a co-op multiplayer mod for the Finnish survival/car game **My Winter Car** (sequel of My Summer Car, set in 1990s rural Finland). There are two UIs:
- the **launcher** (MWCoop.exe, a Windows app: host/join a game, lobby, outfit and car customization, graphics options, mods sharing, Steam/IP network, logs);
- the **in-game coop menus** (F10 menu, drone camera, cheats panel F7, map M, wallet, respawn, shop/VHS editing, notifications).

## Input
Each line of your input file is: `ID <TAB> ENGLISH <TAB> SOURCE FILE <TAB> FRENCH (context, may be empty)`.
The English text is written **as it appears in C++/C# source code**: escapes like `\n`, `\"`, `\\`, `é`, `…` are literal backslash sequences.

## Output
Write a UTF-8 file with one line per translated entry: `ID <TAB> RUSSIAN` (nothing else, no header, no quotes around).
- Write the Russian **in the same source-code escaping**: a line break is the two characters `\n`, a double quote is `\"`, a backslash is `\\`. Never put a raw `"` or a real tab/newline inside a translation. Cyrillic letters are written directly (UTF-8). `…` may become `…` and `«`/`»` may become `«`/`»`.
- Keep **every format placeholder exactly and in the same order**: `%d`, `%s`, `%ls`, `%.1f`, `%02d`, `%%`, `{0}`, `{1}`… and every `\n` (same count).
- Keep leading/trailing spaces and punctuation of the English (e.g. `" gave you "` → `" дал(а) тебе "`, `"Back at "` → `"Снова: "`, keep a trailing space if the English has one).
- **Skip** (write no line for) entries that should not be translated: French text in the English column, proper names (people, places, Steam, Discord, MSCLoader, Nexus, Radmin VPN, CORRIS, SATSUMA, Kiitos…), artwork authors, licenses (CC BY…, Public domain…), dates, file names, codes/units alone (`30 m`, `1.5 km`, `x64`), values identical in Russian, and technical/API identifiers.
- Artwork titles in the credits (e.g. "The cliffs of Bonifacio") **should** be translated.

## Style
- Informal "ты" (the French original uses "tu"), friendly and short. UI space is limited: Russian must stay **as short as possible**, close to the English length. Prefer concise words; abbreviate where natural (мин, сек → "с", км, м, КБ, МБ).
- ALL CAPS English → ALL CAPS Russian. Title case buttons → normal Russian capitalization (first word only).
- Keys stay in Latin: F, R, M, F5, F7, F10, Esc, Enter, Tab, Shift, Ctrl, Q/E, LMB → ЛКМ, RMB → ПКМ, "Hold F" → "Удерживай F".
- Numbers like "1.5 km" keep the dot only if it is inside a placeholder; otherwise use Russian decimal comma.

## Glossary (use consistently)
| English | Russian |
|---|---|
| co-op | кооп |
| host (noun, the player who hosts) | хост |
| HOST (launcher button) / clicking HOST | СОЗДАТЬ / нажав СОЗДАТЬ |
| JOIN (button) | ПОДКЛЮЧИТЬСЯ |
| PLAY SOLO | ИГРАТЬ ОДНОМУ |
| PLAY | ИГРАТЬ |
| guest | гость |
| lobby | лобби |
| launcher | лаунчер |
| game save / save | сохранение |
| mod / mods | мод / моды |
| player name / nickname | ник |
| outfit | одежда (an outfit = комплект одежды; "Outfit " prefix = "Одежда ") |
| appearance | внешность |
| top (clothing) / pants | верх / штаны |
| build (body) | телосложение |
| voice chat / push-to-talk | голосовой чат / рация (кнопка рации) |
| wallet | кошелёк |
| cash | наличные |
| cheats | читы |
| map | карта |
| drone (camera) | дрон |
| respawn | возрождение |
| shop / store | магазин |
| tape (VHS) | кассета |
| VCR | видеомагнитофон |
| camcorder | видеокамера |
| editing (video) | монтаж |
| export | экспорт |
| firewall | брандмауэр |
| router | роутер |
| port forwarding / forwarded | проброс порта / проброшен |
| lobby invite | приглашение |
| settings / options | настройки |
| graphics | графика |
| shadows | тени |
| ray tracing | трассировка лучей |
| reflections | отражения |
| anti-aliasing | сглаживание |
| draw distance | дальность прорисовки |
| field of view | поле зрения |
| head bob | покачивание головы |
| logs | журналы (логи) — use "логи" |
| update (noun) / to update | обновление / обновить |
| markka (money, "mk") | мк |
| rally | ралли |
| passenger / driver | пассажир / водитель |
| seatbelt | ремень |
| trailer / tow rope | прицеп / трос |
| Off / On (toggle values) | Выкл / Вкл |
| Game (value meaning "game default") | Игра |
| Custom | Свой |

Car names (CORRIS, SATSUMA, GIFU, Rivett…), place names (Perajarvi, Peräjärvi, Loppe…), brand names stay as they are.

Before writing, read your whole input file. Translate every line that should be translated, then write the output file with the Write tool. Finish with a short note: how many lines translated, how many skipped.
