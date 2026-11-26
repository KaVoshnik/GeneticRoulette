# Genetic Disease Roulette

A BepInEx mod for **Casualties Unknown**. At the start of every run a roulette spins and gives your character a random inherited or chronic condition: colour blindness, hemophilia, diabetes, epilepsy and more. Some conditions come with a small perk, and most of them can be managed with treatment items that spawn in the world.

> 🇷🇺 Русская версия — ниже / Russian version is below.

## Features

- Roulette at the start of a run (1–3 conditions, configurable; a "healthy" result is possible).
- 14 conditions, each with its own effects (see the table).
- Optional small **perks** for some conditions (can be turned off for pure hardcore).
- **Treatment items** with their own liquids and descriptions: inhaler (own sprite), antiretroviral syringe, insulin syringe (the game's syringe sprite, the liquid colour is visible inside).
- Status icons ("moodles") in the game's own panel for every condition.
- **Banner on the right side of the health menu (R)**, under the skills panel, with the condition, its description and perks.
- Conditions are appended to the **mood line on the death screen** after a `;`.
- Everything is tunable in the config. Russian and English texts.

## Conditions

| Condition | Effect | Perk |
|---|---|---|
| HIV | Wounds get infected ×3 faster | – |
| Hemophilia | Heavier bleeding, slower clotting | No pulmonary embolism |
| Asthma | Periodic attacks: oxygen and stamina drop | – |
| Hearing loss | Permanent hearing loss | Mood is not affected by the permanent loss |
| Narcolepsy | Sudden sleep attacks, you drop what you hold | – |
| Brittle bones | Impacts hurt and break more | – |
| Deuteranopia / Protanopia / Tritanopia | Colour-blind filter (Machado 2009 matrices) | – |
| Achromatopsia | The world is grayscale | – |
| Anemia | Blood volume can never rise above a cap (80% by default) | – |
| Type 1 diabetes | Blood sugar rises over time and with food; needs insulin. High sugar: thirst, weakness, nausea. Low sugar: shaking, hunger, passing out | – |
| Epilepsy | Seizures: screen shake, reversed controls, then you collapse (built from the game's own brain-damage effects) | – |
| Pain insensitivity | Pain and pain shock are almost gone, but impacts hurt ×1.25 more | Pain barely bothers you |

Colour-blindness types exclude each other, so you never get two of them at once.

## Treatment items

| Item | Treats | How it works |
|---|---|---|
| Inhaler (`gr_inhaler`) | Asthma | 10 ml per use. Ends an attack immediately and prevents new ones for a while |
| Antiretroviral syringe (`gr_antiretroviral`) | HIV | Suppresses the virus for a time that depends on the injected dose, so infection chance is back to normal |
| Insulin syringe (`gr_insulin`) | Diabetes | Lowers blood sugar. Too large a dose causes hypoglycemia |

- Syringes use the game's own syringe minigame, so **you choose the dose**.
- The medicines are regular `drug` category items: they appear in loot, on corpses and in traders' stock.
- With `StarterKit = true` a new run starts with one matching item lying next to you.
- Liquid colours were picked to be different from every liquid in the base game (pink, dark blue and green).

### Diabetes in short

Eating raises sugar (more food = bigger spike), sugary drinks add more. Without insulin sugar slowly climbs. Insulin makes it drop for a while. Above ~75 you get thirsty, weak and sick; below ~30 you shake, get hungry and below ~12 you start to lose consciousness. In debug mode the moodle shows the exact value.

## Installation

The game runs on Unity Mono (x64), so **BepInEx 5 x64** is required.

1. Download `BepInEx_win_x64_5.4.23.x` from <https://github.com/BepInEx/BepInEx/releases> and unpack it into the game folder (next to `CasualtiesUnknown.exe`, so that `winhttp.dll` and the `BepInEx` folder sit beside it).
2. Start the game once and close it. BepInEx creates `BepInEx/plugins`, `BepInEx/config` and `LogOutput.log`.
3. Copy `GeneticRoulette.dll` **and the `Sprites` folder** into `BepInEx/plugins/GeneticRoulette/` (the `Sprites` folder must sit next to the DLL).
4. Start a new run.

On Linux/Proton add `WINEDLLOVERRIDES="winhttp=n,b"` to the launch options.

If something does not work, open `BepInEx/LogOutput.log` and look for `GeneticRoulette`, `COMPAT` and `error`. When part of the mod cannot hook into the game (e.g. after a game update) you also get an in-game toast.

## Debug mode

Debug mode is off at every launch.

| Keys | Action |
|---|---|
| `Ctrl+Shift+F9` | Toggle debug mode (a `DEBUG MOD` label appears in the top-right corner) |
| `F8` | Re-roll the roulette (debug mode only) |
| `F6` | Spawn all medicines next to you (debug mode only) |
| `F7` | Show the list of your conditions |

To test a specific condition set `ForceDiseases = Diabetes` (comma separated) in the config.

## Custom sprites

Syringes need no sprite: they use the game's own syringe and the liquid colour (pink for the antiretroviral drug, dark blue for insulin) is visible inside.

Only the **inhaler** has its own sprite. A simple placeholder ships with the mod; replace it with your own PNG at `BepInEx/plugins/GeneticRoulette/Sprites/inhaler.png`:

- Recommended size **7×9 px** (the game's small bottles are 4×7 to 6×8; any size works, the collider follows the sprite).
- PNG with transparency, no anti-aliasing (point filtering is applied), 8 pixels per world unit (the game's value), pivot in the centre.
- The mod looks for `inhaler.png` next to the DLL, in `Sprites` next to the DLL, and then in any folder under `BepInEx/plugins` (case-insensitive). If it is not found, the built-in blue inhaler sprite is used. `LogOutput.log` always says which one was used (search for `Спрайт gr_inhaler`) and, when the file is missing, where the mod looked.

## Configuration

`BepInEx/config/geneticroulette.casualtiesunknown.cfg` is created on first launch. The main options:

- `General`: `Language` (ru / en / auto), `RollCount`, `HealthyChance`, `ShowRoulette`, `EnablePerks`, `ShowHealthBanner`, `ShowOnDeathScreen`.
- `Items`: `StarterKit`, `SpawnInLoot`.
- `Tuning`: every number from the table above (infection multipliers, intervals of attacks, sugar rates, cap of blood volume and so on).
- `Debug`: keys and `ForceDiseases`.

## Building from source

Reference `BepInEx.dll`, `0Harmony.dll` and the game's `Assembly-CSharp.dll`, `UnityEngine*.dll`, `Unity.TextMeshPro.dll`, `Unity.RenderPipelines.Core.Runtime.dll`, `Unity.RenderPipelines.Universal.Runtime.dll`. Source files: `Plugin.cs`, `Diseases.cs`, `CustomItems.cs`, `HealthBanner.cs`, `Moodles.cs`, `UI.cs`.

Adding a condition: add a value to `enum Dz` and one `DiseaseDef` block in `Diseases.Build()` (names, weight, incompatibilities, hooks, moodle, optional perk).

## Known limitations

- The roulette result is stored in one file (`GeneticRoulette.txt`), the game has a single save slot.
- Save files containing the mod's items need the mod to load. Remove the items before uninstalling.
- Several effects patch private game code and may break after a game update. The mod reports such cases in the log and in the game.
- Treatment timers (inhaler, antiretroviral, insulin) are not saved and reset on load.

---

# Рулетка генетических болезней (RU)

Мод для **Casualties Unknown** на BepInEx. В начале каждого забега крутится рулетка и выдаёт персонажу случайное наследственное или хроническое заболевание: дальтонизм, гемофилию, диабет, эпилепсию и другие. У некоторых болезней есть небольшие плюсы, а большинство можно лечить предметами, которые встречаются в мире.

## Что есть в моде

- Рулетка в начале забега (1–3 болезни, настраивается; может выпасть «здоров»).
- 14 болезней, у каждой свои эффекты.
- Небольшие **плюсы** у некоторых болезней (отключаются в конфиге, если нужен чистый хардкор).
- **Лечебные предметы** со своими жидкостями и описаниями: ингалятор (свой спрайт), шприц с препаратом от ВИЧ, шприц с инсулином (спрайт шприца игры, цвет жидкости виден внутри).
- Статусы в родной панели игры для каждой болезни.
- **Плашка справа в меню здоровья (R)**, под панелью навыков: болезнь, описание и плюсы.
- Болезни дописываются в строку **состояния на экране смерти** через `;` после настроения.
- Всё настраивается в конфиге. Тексты на русском и английском.

## Болезни

| Болезнь | Эффект | Плюс |
|---|---|---|
| ВИЧ | Раны заражаются в 3 раза быстрее | – |
| Гемофилия | Кровотечения сильнее, свёртывание хуже | Не бывает лёгочной эмболии |
| Астма | Приступы: падают кислород и выносливость | – |
| Тугоухость | Постоянная потеря слуха | Постоянная потеря слуха не портит настроение |
| Нарколепсия | Внезапные приступы сна, предметы выпадают из рук | – |
| Хрупкие кости | Удары ранят и ломают сильнее | – |
| Дейтеранопия / протанопия / тританопия | Фильтр дальтонизма (матрицы Machado 2009) | – |
| Ахроматопсия | Мир в оттенках серого | – |
| Анемия | Объём крови не поднимается выше порога (по умолчанию 80%) | – |
| Диабет 1 типа | Сахар растёт со временем и от еды, нужен инсулин. Высокий сахар: жажда, слабость, тошнота. Низкий: дрожь, голод, потеря сознания | – |
| Эпилепсия | Припадки: тряска экрана, инвертированное управление, падение (собраны из родных «мозговых» эффектов игры) | – |
| Нечувствительность к боли | Боль и болевой шок почти исчезают, но удары бьют ×1.25 сильнее | Боль почти не мешает |

Виды дальтонизма исключают друг друга.

## Лечение

| Предмет | От чего | Как работает |
|---|---|---|
| Ингалятор (`gr_inhaler`) | Астма | 10 мл за применение. Сразу прекращает приступ и защищает от новых на время |
| Шприц с антиретровирусным препаратом (`gr_antiretroviral`) | ВИЧ | Подавляет вирус на время, зависящее от дозы: шанс заражения как у здорового |
| Шприц с инсулином (`gr_insulin`) | Диабет | Снижает сахар. Слишком большая доза вызывает гипогликемию |

- Шприцы работают через родную мини-игру укола, поэтому **дозу выбираете вы**.
- Лекарства относятся к категории `drug`: попадаются в добыче, на трупах и у торговцев.
- При `StarterKit = true` в начале нового забега рядом лежит подходящий предмет.
- Цвета жидкостей подобраны так, чтобы не совпадать ни с одной жидкостью игры (розовый, тёмно-синий и зелёный).

## Установка

Игра на Unity Mono x64, нужен **BepInEx 5 x64**.

1. Скачайте `BepInEx_win_x64_5.4.23.x` на <https://github.com/BepInEx/BepInEx/releases> и распакуйте в папку игры (рядом с `CasualtiesUnknown.exe`).
2. Запустите игру один раз и закройте.
3. Скопируйте `GeneticRoulette.dll` **и папку `Sprites`** в `BepInEx/plugins/GeneticRoulette/` (папка `Sprites` должна лежать рядом с DLL).
4. Начните новый забег.

В Linux/Proton добавьте в параметры запуска `WINEDLLOVERRIDES="winhttp=n,b"`.

Если что-то не работает, смотрите `BepInEx/LogOutput.log` (ищите `GeneticRoulette`, `COMPAT`, `error`). Если часть мода не смогла подключиться к игре (например, после её обновления), вы увидите уведомление в игре.

## Режим отладки

При каждом запуске игры выключен.

| Клавиши | Действие |
|---|---|
| `Ctrl+Shift+F9` | Включить или выключить отладку (в правом верхнем углу надпись `DEBUG MOD`) |
| `F8` | Перебросить рулетку (только в отладке) |
| `F6` | Выдать все лекарства рядом с игроком (только в отладке) |
| `F7` | Показать список ваших болезней |

Чтобы проверить конкретную болезнь, задайте в конфиге `ForceDiseases = Diabetes` (через запятую).

## Свои спрайты

Шприцам спрайты не нужны: берётся шприц игры, а цвет жидкости (розовый у препарата от ВИЧ, тёмно-синий у инсулина) виден внутри.

Свой спрайт только у **ингалятора**. С модом идёт простая заготовка; замените её своим PNG: `BepInEx/plugins/GeneticRoulette/Sprites/inhaler.png`.

- Рекомендуемый размер **7×9 пикселей** (маленькие флаконы игры от 4×7 до 6×8; подойдёт любой, коллайдер подстраивается под спрайт).
- PNG с прозрачностью, без сглаживания (применяется point-фильтр), 8 пикселей на единицу мира (как в игре), центр спрайта посередине.
- Мод ищет `inhaler.png` рядом с DLL, в папке `Sprites` рядом с DLL, а затем в любой папке внутри `BepInEx/plugins` (регистр не важен). Если файла нет, используется встроенный синий ингалятор. В `LogOutput.log` всегда написано, какой спрайт взят (ищите `Спрайт gr_inhaler`), а если файл не найден, ещё и где мод его искал.

## Конфиг

`BepInEx/config/geneticroulette.casualtiesunknown.cfg` создаётся при первом запуске. Основные секции: `General` (язык, число болезней, шанс «здоров», плюсы, плашка, экран смерти), `Items` (стартовый набор, добыча), `Tuning` (все числа эффектов), `Debug` (клавиши и принудительные болезни).

## Сборка

Нужны ссылки на `BepInEx.dll`, `0Harmony.dll` и сборки игры: `Assembly-CSharp.dll`, `UnityEngine*.dll`, `Unity.TextMeshPro.dll`, `Unity.RenderPipelines.Core.Runtime.dll`, `Unity.RenderPipelines.Universal.Runtime.dll`. Файлы: `Plugin.cs`, `Diseases.cs`, `CustomItems.cs`, `HealthBanner.cs`, `Moodles.cs`, `UI.cs`.

Новая болезнь: значение в `enum Dz` и один блок `DiseaseDef` в `Diseases.Build()`.

## Известные ограничения

- Результат рулетки хранится в одном файле (`GeneticRoulette.txt`), у игры один слот сохранения.
- Сохранения с предметами мода без мода не загрузятся. Перед удалением мода выбросьте их.
- Часть эффектов патчит закрытый код игры и может сломаться после обновления. Мод сообщит об этом в логе и в игре.
- Таймеры лечения (ингалятор, препарат от ВИЧ, инсулин) не сохраняются и сбрасываются при загрузке.