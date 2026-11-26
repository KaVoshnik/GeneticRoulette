using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GeneticRoulette
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "geneticroulette.casualtiesunknown";
        public const string PluginName = "Genetic Disease Roulette";
        public const string PluginVersion = "1.2.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // ---------- конфиг ----------
        internal static ConfigEntry<string> CfgLang;
        internal static ConfigEntry<int> CfgRollCount;
        internal static ConfigEntry<float> CfgHealthyChance;
        internal static ConfigEntry<bool> CfgShowRoulette;
        internal static ConfigEntry<KeyCode> CfgListKey;
        internal static ConfigEntry<KeyCode> CfgDebugKey;
        internal static ConfigEntry<KeyCode> CfgRerollKey;
        internal static ConfigEntry<string> CfgForce;
        internal static ConfigEntry<bool> CfgSkipTutorial;
        internal static ConfigEntry<string> CfgAccent;
        internal static ConfigEntry<float> CfgUiScale;
        internal static ConfigEntry<bool> CfgPerks;
        internal static ConfigEntry<bool> CfgBanner;
        internal static ConfigEntry<bool> CfgDeathStat;
        internal static ConfigEntry<bool> CfgStarterKit;
        internal static ConfigEntry<bool> CfgItemsInLoot;
        internal static ConfigEntry<KeyCode> CfgGiveKey;
        internal static ConfigEntry<float> CfgAnemiaCap;
        internal static ConfigEntry<float> CfgSugarRise;
        internal static ConfigEntry<float> CfgInsulinPower;
        internal static ConfigEntry<float> CfgSugarPerFood;
        internal static ConfigEntry<float> CfgSugarPerDrink;
        internal static ConfigEntry<float> CfgInsulinSecPerMl;
        internal static ConfigEntry<float> CfgInsulinImmediate;
        internal static ConfigEntry<float> CfgEpiMin;
        internal static ConfigEntry<float> CfgEpiMax;
        internal static ConfigEntry<float> CfgPainCap;
        internal static ConfigEntry<float> CfgPainBlindDamage;
        internal static ConfigEntry<float> CfgArvSecPerMl;
        internal static ConfigEntry<float> CfgArvMax;
        internal static ConfigEntry<float> CfgInhalerSecPerMl;
        internal static ConfigEntry<float> CfgInhalerMax;
        internal static ConfigEntry<float> CfgHivInfection;
        internal static ConfigEntry<float> CfgHemoBleed;
        internal static ConfigEntry<float> CfgHemoClot;
        internal static ConfigEntry<float> CfgAsthmaMin;
        internal static ConfigEntry<float> CfgAsthmaMax;
        internal static ConfigEntry<float> CfgAsthmaFloor;
        internal static ConfigEntry<float> CfgDeafFloor;
        internal static ConfigEntry<float> CfgNarcMin;
        internal static ConfigEntry<float> CfgNarcMax;
        internal static ConfigEntry<float> CfgNarcSleep;
        internal static ConfigEntry<float> CfgBones;

        // ---------- состояние ----------
        internal static HashSet<Dz> Active = new HashSet<Dz>();
        internal static bool Has(Dz d) { return Active.Contains(d); }

        /// <summary>Режим отладки. Включается Ctrl+Shift+DebugKey, при каждом запуске игры выключен.</summary>
        internal static bool DebugMode;

        /// <summary>Тело игрока; обновляется раз в кадр в Step, чтобы не искать его в каждом патче.</summary>
        internal static Body PlayerBody;

        /// <summary>Проблемы, найденные при старте (не нашлись методы/поля игры); о них один раз скажет тост.</summary>
        internal static readonly List<string> Problems = new List<string>();
        static bool problemsShown;

        static bool pendingInit;
        static bool pendingLoaded;

        // рулетка
        static bool rouletteActive;
        static List<int> rolls = new List<int>();
        static float rouletteStart;
        const float SpinTime = 2.4f;
        const float SlotTime = 4.4f;
        static int rollIdx;
        static bool locked;
        static string reelText = "";
        static float reelTimer;

        // HUD
        static string toastText = "";
        static float toastUntil;
        static float listUntil;
        static bool showList;
        static bool legacyInputOk = true;
        static float lastKeyTime;
        static WorldGeneration lastWorld;
        static float waitLogTime;
        static bool lastLoadFlagKnown;

        // зрение
        GameObject visionGo;
        float visionCheck;

        readonly RouletteUi ui = new RouletteUi();
        static float spinProgress;

        // =====================================================================
        void Awake()
        {
            Instance = this;
            Log = Logger;

            CfgLang = Config.Bind("General", "Language", "ru", "ru / en / auto (по языку игры)");
            CfgRollCount = Config.Bind("General", "RollCount", 1, "Сколько болезней выпадает за забег (1-3)");
            CfgHealthyChance = Config.Bind("General", "HealthyChance", 0.10f, "Шанс, что в одном броске выпадет 'Здоров' (0-1)");
            CfgShowRoulette = Config.Bind("General", "ShowRoulette", true, "Показывать анимацию рулетки в начале забега");
            CfgListKey = Config.Bind("General", "ListKey", KeyCode.F7, "Клавиша показа списка текущих болезней");
            CfgDebugKey = Config.Bind("Debug", "DebugToggleKey", KeyCode.F9, "Ctrl+Shift+эта клавиша включает/выключает режим отладки (в углу экрана появится надпись DEBUG MOD)");
            CfgRerollKey = Config.Bind("Debug", "RerollKey", KeyCode.F8, "Тест: принудительно бросить рулетку заново. Работает только при включённом режиме отладки");
            CfgForce = Config.Bind("Debug", "ForceDiseases", "", "Для теста: принудительный список через запятую (Hiv,Hemophilia,Asthma,Deafness,Narcolepsy,FragileBones,Deuteranopia,Achromatopsia). Пусто = случайно");
            CfgAccent = Config.Bind("UI", "AccentColor", "#C93C3C", "Цвет рамок и акцентов интерфейса (HTML-цвет)");
            CfgUiScale = Config.Bind("UI", "UiScale", 1.0f, "Масштаб окон мода (0.5-2.5)");
            CfgSkipTutorial = Config.Bind("Debug", "SkipInTutorial", false, "Не бросать рулетку, если в сцене есть TutorialHandler");
            CfgPerks = Config.Bind("General", "EnablePerks", true, "Небольшие плюсы у некоторых болезней (баланс). false = только штрафы, чистый хардкор");
            CfgBanner = Config.Bind("General", "ShowHealthBanner", true, "Плашка с болезнью, описанием и плюсами сверху в меню здоровья (R)");
            CfgDeathStat = Config.Bind("General", "ShowOnDeathScreen", true, "Дописывать болезнь к строке состояния на экране смерти");
            CfgStarterKit = Config.Bind("Items", "StarterKit", true, "Выдавать в начале нового забега лекарство для болезни (ингалятор, инсулин, препарат от ВИЧ)");
            CfgItemsInLoot = Config.Bind("Items", "SpawnInLoot", true, "Лекарства мода встречаются в мире как обычная добыча категории 'drug'");
            CfgGiveKey = Config.Bind("Debug", "GiveItemsKey", KeyCode.F6, "Тест: выдать все лекарства мода. Работает только при включённом режиме отладки");
            CfgAnemiaCap = Config.Bind("Tuning", "AnemiaBloodCap", 80f, "Анемия: объём крови не поднимается выше этого значения (100 = норма)");
            CfgSugarRise = Config.Bind("Tuning", "SugarRisePerSecond", 0.04f, "Диабет: на сколько в секунду растёт сахар без инсулина");
            CfgInsulinPower = Config.Bind("Tuning", "InsulinSugarDropPerSecond", 0.15f, "Диабет: на сколько в секунду падает сахар, пока действует инсулин");
            CfgSugarPerFood = Config.Bind("Tuning", "SugarPerFoodUnit", 1.2f, "Диабет: прирост сахара за единицу сытости от еды");
            CfgSugarPerDrink = Config.Bind("Tuning", "SugarPerSugaryDrinkMl", 0.12f, "Диабет: прирост сахара за мл сладкого напитка");
            CfgInsulinSecPerMl = Config.Bind("Tuning", "InsulinSecondsPerMl", 5f, "Секунд действия инсулина на 1 мл");
            CfgInsulinImmediate = Config.Bind("Tuning", "InsulinImmediateDropPerMl", 0.6f, "Мгновенное снижение сахара на 1 мл инсулина");
            CfgEpiMin = Config.Bind("Tuning", "EpilepsyIntervalMin", 150f, "Мин. секунд между припадками");
            CfgEpiMax = Config.Bind("Tuning", "EpilepsyIntervalMax", 330f, "Макс. секунд между припадками");
            CfgPainCap = Config.Bind("Tuning", "PainInsensitivityPainCap", 6f, "Максимальная боль каждой конечности при нечувствительности к боли");
            CfgPainBlindDamage = Config.Bind("Tuning", "PainInsensitivityImpactMultiplier", 1.25f, "Множитель силы ударов при нечувствительности к боли");
            CfgArvSecPerMl = Config.Bind("Tuning", "AntiretroviralSecondsPerMl", 12f, "Секунд подавления ВИЧ на 1 мл препарата");
            CfgArvMax = Config.Bind("Tuning", "AntiretroviralMaxSeconds", 900f, "Максимум накопленного подавления ВИЧ, секунд");
            CfgInhalerSecPerMl = Config.Bind("Tuning", "InhalerSecondsPerMl", 12f, "Секунд защиты от астмы на 1 мл бронхолитика (доза ингалятора 10 мл)");
            CfgInhalerMax = Config.Bind("Tuning", "InhalerMaxSeconds", 600f, "Максимум накопленной защиты от астмы, секунд");
            CfgHivInfection = Config.Bind("Tuning", "HivInfectionMultiplier", 3.0f, "Множитель шанса заражения ран");
            CfgHemoBleed = Config.Bind("Tuning", "HemophiliaBleedMultiplier", 1.8f, "Множитель скорости кровотечения");
            CfgHemoClot = Config.Bind("Tuning", "HemophiliaClotMultiplier", 0.4f, "Множитель скорости свёртывания (меньше = хуже)");
            CfgAsthmaMin = Config.Bind("Tuning", "AsthmaIntervalMin", 150f, "Мин. секунд между приступами астмы");
            CfgAsthmaMax = Config.Bind("Tuning", "AsthmaIntervalMax", 330f, "Макс. секунд между приступами астмы");
            CfgAsthmaFloor = Config.Bind("Tuning", "AsthmaOxygenFloor", 72f, "Ниже этого уровня кислорода приступ его не опускает (смерть при <65)");
            CfgDeafFloor = Config.Bind("Tuning", "DeafnessFloor", 45f, "Минимальная потеря слуха, 0-100");
            CfgNarcMin = Config.Bind("Tuning", "NarcolepsyIntervalMin", 120f, "Мин. секунд между приступами сна");
            CfgNarcMax = Config.Bind("Tuning", "NarcolepsyIntervalMax", 300f, "Макс. секунд между приступами сна");
            CfgNarcSleep = Config.Bind("Tuning", "NarcolepsySleepSeconds", 20f, "Через сколько секунд будить после приступа (0 = как решит игра)");
            CfgBones = Config.Bind("Tuning", "BrittleBonesImpactMultiplier", 1.45f, "Множитель силы ударов по конечностям");

            Diseases.Init();

            try
            {
                GameObject go = new GameObject("GeneticRoulette_Runner");
                go.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(go);
                go.AddComponent<Runner>();
                Log.LogInfo("Runner created");
            }
            catch (Exception e) { Log.LogError("Runner creation failed: " + e); Problem("Не удалось создать Runner: мод не будет работать"); }

            var harmony = new Harmony(PluginGuid);
            int ok = 0, fail = 0;
            foreach (Type t in typeof(Plugin).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try { harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception e)
                {
                    fail++;
                    Log.LogError("Patch " + t.Name + " failed: " + e);
                    Problem("Патч " + t.Name + " не применился");
                }
            }
            Log.LogInfo(PluginName + " " + PluginVersion + ": patches ok=" + ok + " failed=" + fail);
        }

        /// <summary>Запоминает проблему совместимости с версией игры (пишет в лог и покажет тост в игре).</summary>
        internal static void Problem(string text)
        {
            Problems.Add(text);
            if (Log != null) Log.LogError("COMPAT: " + text);
        }

        // вызывается из патча WorldGeneration.Start -> SaveSystem.TryLoadGame
        internal static void OnWorldStart(bool loadingSave)
        {
            pendingInit = true;
            pendingLoaded = loadingSave;
            lastLoadFlagKnown = true;
            Log.LogInfo("TryLoadGame called, loadingSave=" + loadingSave);
        }

        void OnDestroy()
        {
            Log.LogWarning("Plugin object was destroyed by Unity (Runner keeps working)");
        }

        internal static bool IsPlayer(Body b)
        {
            return ReferenceEquals(b, PlayerBody) && (object)PlayerBody != null;
        }

        static void RefreshPlayer()
        {
            PlayerCamera cam = PlayerCamera.main;
            PlayerBody = cam != null ? cam.body : null;
        }

        // =====================================================================
        // Инициализация забега
        // =====================================================================
        void TryInit()
        {
            PlayerCamera cam = PlayerCamera.main;
            WorldGeneration w = WorldGeneration.world;
            if (cam == null || cam.body == null || w == null || w.generatingWorld || !w.worldExists)
            {
                if (Time.realtimeSinceStartup - waitLogTime > 5f)
                {
                    waitLogTime = Time.realtimeSinceStartup;
                    Log.LogInfo("Waiting for world: cam=" + (cam != null) + " body=" + (cam != null && cam.body != null)
                        + " world=" + (w != null) + " generating=" + (w != null && w.generatingWorld)
                        + " exists=" + (w != null && w.worldExists));
                }
                return;
            }

            pendingInit = false;
            lastWorld = w;
            Log.LogInfo("World ready, initializing (loaded=" + pendingLoaded + ")");

            if (Problems.Count > 0 && !problemsShown)
            {
                problemsShown = true;
                Toast((Ru() ? "Genetic Roulette: часть эффектов не работает (" : "Genetic Roulette: some effects are off (") + Problems.Count
                    + (Ru() ? "), см. лог BepInEx" : "), see BepInEx log"), 9f);
            }

            if (CfgSkipTutorial.Value)
            {
                try
                {
                    if (UnityEngine.Object.FindObjectOfType<TutorialHandler>() != null)
                    {
                        Active.Clear();
                        return;
                    }
                }
                catch (Exception) { }
            }

            ResetEffectState();

            if (pendingLoaded)
            {
                HashSet<Dz> saved = LoadSidecar();
                if (saved != null)
                {
                    Active = saved;
                    listUntil = Time.realtimeSinceStartup + 8f;
                    EnsureVision(true);
                    Log.LogInfo("Loaded run diseases: " + Describe(Active));
                    return;
                }
            }

            NewRoll(true);
        }

        void NewRoll(bool giveKit)
        {
            rolls = DoRoll();
            Active = new HashSet<Dz>();
            foreach (int r in rolls) if (r >= 0) Active.Add((Dz)r);
            SaveSidecar();
            EnsureVision(true);
            Log.LogInfo("New run diseases: " + Describe(Active));
            if (giveKit) CustomItems.GiveStarterKit(PlayerBody);

            if (CfgShowRoulette.Value)
            {
                rouletteActive = true;
                rouletteStart = Time.realtimeSinceStartup;
                reelTimer = 0f;
                locked = false;
            }
            else
            {
                listUntil = Time.realtimeSinceStartup + 10f;
            }
        }

        static void ResetEffectState()
        {
            Diseases.ResetAll();
        }

        static DiseaseDef PickWeighted(List<DiseaseDef> pool)
        {
            float total = 0f;
            foreach (DiseaseDef d in pool) total += Mathf.Max(0f, d.Weight);
            if (total <= 0f) return pool[UnityEngine.Random.Range(0, pool.Count)];
            float r = UnityEngine.Random.value * total;
            foreach (DiseaseDef d in pool)
            {
                r -= Mathf.Max(0f, d.Weight);
                if (r <= 0f) return d;
            }
            return pool[pool.Count - 1];
        }

        static List<int> DoRoll()
        {
            List<int> result = new List<int>();

            string forced = CfgForce.Value;
            if (!string.IsNullOrEmpty(forced) && forced.Trim().Length > 0)
            {
                foreach (string part in forced.Split(','))
                {
                    try
                    {
                        Dz d = (Dz)Enum.Parse(typeof(Dz), part.Trim(), true);
                        if (Diseases.Get(d) != null && !result.Contains((int)d)) result.Add((int)d);
                    }
                    catch (Exception) { Log.LogWarning("ForceDiseases: unknown name '" + part + "'"); }
                }
                if (result.Count > 0) return result;
            }

            List<DiseaseDef> pool = new List<DiseaseDef>(Diseases.All);
            int n = Mathf.Clamp(CfgRollCount.Value, 1, 3);
            for (int i = 0; i < n; i++)
            {
                if (UnityEngine.Random.value < CfgHealthyChance.Value || pool.Count == 0)
                {
                    result.Add(-1);
                    continue;
                }
                DiseaseDef pick = PickWeighted(pool);
                result.Add((int)pick.Id);
                pool.Remove(pick);
                // выкидываем несовместимые (в обе стороны)
                Dz picked = pick.Id;
                pool.RemoveAll(p => Array.IndexOf(pick.Incompatible, p.Id) >= 0 || Array.IndexOf(p.Incompatible, picked) >= 0);
            }
            return result;
        }

        // ---------- сохранение рядом с save.sv ----------
        static string SidecarPath()
        {
            return Path.Combine(Application.persistentDataPath, "GeneticRoulette.txt");
        }

        static void SaveSidecar()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Dz d in Active) { if (sb.Length > 0) sb.Append(','); sb.Append(d.ToString()); }
                File.WriteAllText(SidecarPath(), sb.Length == 0 ? "none" : sb.ToString());
            }
            catch (Exception e) { Log.LogWarning("Could not write sidecar: " + e.Message); }
        }

        static HashSet<Dz> LoadSidecar()
        {
            try
            {
                string p = SidecarPath();
                if (!File.Exists(p)) return null;
                string txt = File.ReadAllText(p).Trim();
                HashSet<Dz> set = new HashSet<Dz>();
                if (txt == "none" || txt.Length == 0) return set;
                foreach (string part in txt.Split(','))
                {
                    try
                    {
                        Dz d = (Dz)Enum.Parse(typeof(Dz), part.Trim(), true);
                        if (Diseases.Get(d) != null) set.Add(d);
                    }
                    catch (Exception) { }
                }
                return set;
            }
            catch (Exception e) { Log.LogWarning("Could not read sidecar: " + e.Message); return null; }
        }

        // ---------- экран смерти ----------
        /// <summary>
        /// Строка «состояния» на экране смерти (там, где пишут настроение): добавляем болезни через «; ».
        /// Вызывается транспилером из PlayerCamera.EndSequence сразу после ToUpper настроения.
        /// </summary>
        public static string DeathState(string mood)
        {
            try
            {
                if (!CfgDeathStat.Value || Active.Count == 0) return mood;
                bool ru = Ru();
                TMP_Text t = null;
                try { t = PlayerCamera.main.deathStats.GetChild(7).GetComponent<TMP_Text>(); } catch (Exception) { }
                if (ru && t != null && t.font != null && !t.font.HasCharacter('Ж', true)) ru = false;   // шрифт без кириллицы
                List<string> names = new List<string>();
                foreach (DiseaseDef d in Diseases.All) if (Has(d.Id)) names.Add(d.Short(ru));
                if (names.Count == 0) return mood;
                return mood + "; " + string.Join(", ", names.ToArray()).ToUpper();
            }
            catch (Exception e) { Log.LogError("DeathState: " + e); return mood; }
        }

        /// <summary>Подходит ли шрифт для русского текста (иначе показываем английские названия).</summary>
        internal static bool FontHasCyrillic(TMP_Text t)
        {
            try { return t != null && t.font != null && t.font.HasCharacter('Ж', true); }
            catch (Exception) { return false; }
        }

        // ---------- тексты ----------
        internal static bool Ru()
        {
            string l = (CfgLang.Value ?? "ru").Trim().ToLowerInvariant();
            if (l == "ru") return true;
            if (l == "en") return false;
            try
            {
                string n = Locale.currentLangName;
                if (string.IsNullOrEmpty(n)) return false;
                n = n.ToLowerInvariant();
                return n.StartsWith("ru") || n.Contains("рус");
            }
            catch (Exception) { return false; }
        }

        static string DName(Dz d) { DiseaseDef def = Diseases.Get(d); return def == null ? d.ToString() : def.Name(Ru()); }
        static string DDesc(Dz d)
        {
            DiseaseDef def = Diseases.Get(d);
            if (def == null) return "";
            string text = def.Desc(Ru());
            string perk = def.Perk(Ru());
            if (!string.IsNullOrEmpty(perk)) text += "\n" + (Ru() ? "Плюс: " : "Perk: ") + perk;
            return text;
        }
        static string Healthy() { return Ru() ? "Здоров" : "Healthy"; }
        static string RollName(int v) { return v < 0 ? Healthy() : DName((Dz)v); }

        /// <summary>Случайная строка для «барабана»: любая болезнь из реестра или «Здоров».</summary>
        static string RandomReelText()
        {
            int i = UnityEngine.Random.Range(-1, Diseases.All.Count);
            return i < 0 ? Healthy() : Diseases.All[i].Name(Ru());
        }

        static string Describe(HashSet<Dz> set)
        {
            if (set.Count == 0) return "none";
            StringBuilder sb = new StringBuilder();
            foreach (Dz d in set) { if (sb.Length > 0) sb.Append(", "); sb.Append(d.ToString()); }
            return sb.ToString();
        }

        internal static void Toast(string text, float seconds)
        {
            toastText = text;
            toastUntil = Time.realtimeSinceStartup + seconds;
        }

        // =====================================================================
        // Update / UI
        // =====================================================================
        bool stepLogged;

        void HandleKey(KeyCode key, bool ctrl, bool shift)
        {
            if (Time.realtimeSinceStartup - lastKeyTime < 0.25f) return;

            if (key == CfgDebugKey.Value)
            {
                // режим отладки — только по комбинации Ctrl+Shift+клавиша
                if (!(ctrl && shift)) return;
                lastKeyTime = Time.realtimeSinceStartup;
                DebugMode = !DebugMode;
                Log.LogInfo("Debug mode: " + DebugMode);
                Toast(DebugMode
                    ? "DEBUG MOD: ON (" + CfgRerollKey.Value + " = " + (Ru() ? "бросить заново" : "reroll") + ")"
                    : "DEBUG MOD: OFF", 3f);
                return;
            }

            if (key == CfgListKey.Value)
            {
                lastKeyTime = Time.realtimeSinceStartup;
                showList = !showList;
                Log.LogInfo("List toggled: " + showList);
            }
            else if (key == CfgGiveKey.Value)
            {
                if (!DebugMode) return;
                lastKeyTime = Time.realtimeSinceStartup;
                GiveAllTestItems();
            }
            else if (key == CfgRerollKey.Value)
            {
                if (!DebugMode) return;
                lastKeyTime = Time.realtimeSinceStartup;
                Log.LogInfo("Manual reroll (debug mode)");
                ResetEffectState();
                NewRoll(false);
            }
        }

        static void GiveAllTestItems()
        {
            try
            {
                Body b = PlayerBody;
                if (b == null) return;
                int n = 0;
                foreach (CustomItemDef d in CustomItems.Items_)
                {
                    Vector2 pos = (Vector2)b.transform.position + new Vector2(0.6f * (n + 1), 0.8f);
                    Utils.Create(d.Id, pos, 0f);
                    n++;
                }
                Toast("DEBUG MOD: " + (Ru() ? "лекарства выданы" : "medicines spawned"), 3f);
            }
            catch (Exception e) { Log.LogError("GiveAllTestItems: " + e); }
        }

        internal void Step()
        {
            if (!stepLogged) { stepLogged = true; Log.LogInfo("Update loop is running"); }

            RefreshPlayer();

            // новый мир (новая сцена) без вызова TryLoadGame-патча
            WorldGeneration cw = WorldGeneration.world;
            if (!pendingInit && cw != null && cw != lastWorld)
            {
                pendingInit = true;
                pendingLoaded = !lastLoadFlagKnown ? false : pendingLoaded;
                Log.LogInfo("New WorldGeneration instance detected");
            }
            if (pendingInit) TryInit();

            // клавиши
            if (legacyInputOk)
            {
                try
                {
                    bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    if (Input.GetKeyDown(CfgListKey.Value)) HandleKey(CfgListKey.Value, ctrl, shift);
                    if (Input.GetKeyDown(CfgRerollKey.Value)) HandleKey(CfgRerollKey.Value, ctrl, shift);
                    if (Input.GetKeyDown(CfgDebugKey.Value)) HandleKey(CfgDebugKey.Value, ctrl, shift);
                    if (Input.GetKeyDown(CfgGiveKey.Value)) HandleKey(CfgGiveKey.Value, ctrl, shift);
                }
                catch (Exception) { legacyInputOk = false; Log.LogInfo("Legacy Input unavailable, using IMGUI key events"); }
            }

            // периодическая проверка эффекта зрения
            visionCheck -= Time.unscaledDeltaTime;
            if (visionCheck <= 0f)
            {
                visionCheck = 1f;
                EnsureVision(false);
            }

            // анимация рулетки
            if (rouletteActive)
            {
                float t = Time.realtimeSinceStartup - rouletteStart;
                int idx = (int)(t / SlotTime);
                if (idx >= rolls.Count)
                {
                    rouletteActive = false;
                    listUntil = Time.realtimeSinceStartup + 10f;
                }
                else
                {
                    rollIdx = idx;
                    float local = t - idx * SlotTime;
                    locked = local >= SpinTime;
                    spinProgress = Mathf.Clamp01(local / SpinTime);
                    if (locked)
                    {
                        reelText = RollName(rolls[idx]);
                    }
                    else
                    {
                        reelTimer -= Time.unscaledDeltaTime;
                        if (reelTimer <= 0f)
                        {
                            reelText = RandomReelText();
                            float p = local / SpinTime;
                            reelTimer = Mathf.Lerp(0.05f, 0.4f, p * p);
                        }
                    }
                }
            }

            UpdateUi();
        }

        internal void Draw()
        {
            // Отрисовка — в uGUI (RouletteUi); здесь только запасной приём клавиш через IMGUI.
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == CfgListKey.Value || e.keyCode == CfgRerollKey.Value || e.keyCode == CfgDebugKey.Value || e.keyCode == CfgGiveKey.Value)
                    HandleKey(e.keyCode, e.control, e.shift);
            }
        }

        void UpdateUi()
        {
            bool listOn = showList || Time.realtimeSinceStartup < listUntil;
            bool toastOn = Time.realtimeSinceStartup < toastUntil;

            string title = Ru() ? "РУЛЕТКА ГЕНЕТИЧЕСКИХ БОЛЕЗНЕЙ" : "GENETIC DISEASE ROULETTE";
            string counter = "";
            string desc = "";
            string reel = reelText;
            if (rouletteActive && rollIdx < rolls.Count)
            {
                counter = (Ru() ? "БРОСОК " : "ROLL ") + (rollIdx + 1) + "/" + rolls.Count;
                if (locked)
                {
                    int v = rolls[rollIdx];
                    desc = v < 0 ? (Ru() ? "Повезло: никаких болезней." : "Lucky: no diseases.") : DDesc((Dz)v);
                }
            }

            string head = Ru() ? "ВАШИ БОЛЕЗНИ" : "YOUR CONDITIONS";
            StringBuilder body = new StringBuilder();
            int lines = 0;
            if (Active.Count == 0) { body.Append(Healthy()); lines = 1; }
            else
            {
                foreach (Dz d in Active)
                {
                    if (lines > 0) body.Append('\n');
                    body.Append("- ").Append(DName(d));
                    lines++;
                }
            }

            ui.Update(rouletteActive, title, counter, reel, locked, spinProgress, desc,
                listOn, head, body.ToString(), lines, toastOn, toastText, DebugMode);
        }

        // =====================================================================
        // Зрение: URP Volume, профиль собирают сами болезни (DiseaseDef.Vision)
        // =====================================================================
        static bool NeedVision()
        {
            foreach (DiseaseDef d in Diseases.All)
                if (d.Vision != null && Has(d.Id)) return true;
            return false;
        }

        void EnsureVision(bool rebuild)
        {
            bool need = NeedVision();
            if (!need)
            {
                if (visionGo != null) { Destroy(visionGo); visionGo = null; }
                return;
            }
            if (visionGo != null && !rebuild) return;
            if (visionGo != null) { Destroy(visionGo); visionGo = null; }

            try
            {
                visionGo = new GameObject("GeneticRoulette_Vision");
                DontDestroyOnLoad(visionGo);
                Volume vol = visionGo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = 1000f;
                vol.weight = 1f;
                VolumeProfile prof = ScriptableObject.CreateInstance<VolumeProfile>();
                foreach (DiseaseDef d in Diseases.All)
                    if (d.Vision != null && Has(d.Id)) d.Vision(prof);
                vol.sharedProfile = prof;
            }
            catch (Exception e)
            {
                Log.LogError("Vision volume failed: " + e);
            }
        }

        // =====================================================================
        // Эффекты на игроке (вызывается из Body.Update)
        // =====================================================================
        internal static void Tick(Body b)
        {
            if (Active.Count == 0 || pendingInit) return;
            if (!b.alive) return;

            // как и сама игра в HandlePeriodicChecks: во время генерации мира и катсцен ничего не делаем
            WorldGeneration w = WorldGeneration.world;
            if (w != null && (w.generatingWorld || w.cutsceneActive)) return;

            foreach (DiseaseDef d in Diseases.All)
                if (d.Tick != null && Has(d.Id)) d.Tick(b);
        }

        internal static void Periodic(Body b)
        {
            if (Active.Count == 0 || pendingInit) return;
            foreach (DiseaseDef d in Diseases.All)
                if (d.Periodic != null && Has(d.Id)) d.Periodic(b);
        }
    }

    internal class Runner : MonoBehaviour
    {
        void Awake() { Plugin.Log.LogInfo("Runner.Awake"); }

        void Update()
        {
            Plugin p = Plugin.Instance;
            if ((object)p == null) return;
            try { p.Step(); }
            catch (Exception e) { Plugin.Log.LogError("Step failed: " + e); }
        }

        void OnGUI()
        {
            Plugin p = Plugin.Instance;
            if ((object)p == null) return;
            try { p.Draw(); }
            catch (Exception e) { Plugin.Log.LogError("Draw failed: " + e); }
        }
    }

    // =========================================================================
    // Harmony-патчи
    // =========================================================================
    [HarmonyPatch(typeof(SaveSystem), "TryLoadGame")]
    static class Patch_TryLoadGame
    {
        static void Prefix()
        {
            try { Plugin.OnWorldStart(SaveSystem.loadedRun && SaveSystem.HasSave()); }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }

    [HarmonyPatch(typeof(Body), "Update")]
    static class Patch_BodyUpdate
    {
        static void Postfix(Body __instance)
        {
            try { if (Plugin.IsPlayer(__instance)) Plugin.Tick(__instance); }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }

    [HarmonyPatch(typeof(Body), "HandlePeriodicChecks")]
    static class Patch_BodyPeriodic
    {
        static void Postfix(Body __instance)
        {
            try { if (Plugin.IsPlayer(__instance)) Plugin.Periodic(__instance); }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }

    // ВИЧ: Limb.Update читает Body.infectionChanceMultiplier ровно в одном месте.
    // Вместо подмены поля на время вызова заменяем само чтение на вызов Diseases.InfectionMultiplier(body).
    [HarmonyPatch(typeof(Limb), "Update")]
    static class Patch_LimbUpdate
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo replacement = AccessTools.Method(typeof(Diseases), "InfectionMultiplier", new Type[] { typeof(Body) });
            int replaced = 0;
            List<CodeInstruction> result = new List<CodeInstruction>();
            foreach (CodeInstruction ci in code)
            {
                FieldInfo fi = ci.operand as FieldInfo;
                if (ci.opcode == OpCodes.Ldfld && fi != null && fi.DeclaringType == typeof(Body) && fi.Name == "infectionChanceMultiplier")
                {
                    CodeInstruction repl = new CodeInstruction(OpCodes.Call, replacement);
                    repl.labels.AddRange(ci.labels);
                    repl.blocks.AddRange(ci.blocks);
                    result.Add(repl);
                    replaced++;
                }
                else result.Add(ci);
            }
            if (replaced == 0)
                throw new InvalidOperationException("Limb.Update больше не читает Body.infectionChanceMultiplier (версия игры изменилась?)");
            return result;
        }
    }

    // Плюс гемофилии: проверка «вязкость > 90 → лёгочная эмболия» в HandlePeriodicChecks
    // читает вязкость через Diseases.EmbolismViscosity (подмена чтения поля, как у ВИЧ).
    [HarmonyPatch(typeof(Body), "HandlePeriodicChecks")]
    static class Patch_Embolism
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo replacement = AccessTools.Method(typeof(Diseases), "EmbolismViscosity", new Type[] { typeof(Body) });
            List<CodeInstruction> list = new List<CodeInstruction>(code);
            int replaced = 0;
            for (int i = 0; i < list.Count - 1; i++)
            {
                FieldInfo fi = list[i].operand as FieldInfo;
                if (list[i].opcode == OpCodes.Ldfld && fi != null && fi.DeclaringType == typeof(Body) && fi.Name == "bloodViscosity"
                    && list[i + 1].opcode == OpCodes.Ldc_R4 && list[i + 1].operand is float && (float)list[i + 1].operand == 90f)
                {
                    CodeInstruction repl = new CodeInstruction(OpCodes.Call, replacement);
                    repl.labels.AddRange(list[i].labels);
                    repl.blocks.AddRange(list[i].blocks);
                    list[i] = repl;
                    replaced++;
                }
            }
            if (replaced != 1)
                throw new InvalidOperationException("Проверка вязкости крови (>90) в HandlePeriodicChecks не найдена или найдена не одна (версия игры изменилась?): " + replaced);
            return list;
        }
    }

    // Плюс тугоухости: компенсируем часть штрафа настроения от постоянной потери слуха
    [HarmonyPatch(typeof(Body), "totalHappiness", MethodType.Getter)]
    static class Patch_TotalHappiness
    {
        static void Postfix(Body __instance, ref float __result)
        {
            try { __result += Diseases.DeafnessHappinessBonus(__instance); }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }

    // Хрупкие кости: усиливаем силу удара
    [HarmonyPatch(typeof(Limb), "ImpactDamage")]
    static class Patch_LimbImpact
    {
        static void Prefix(Limb __instance, ref float __0)
        {
            try
            {
                if (!Plugin.IsPlayer(__instance.body)) return;
                if (Plugin.Has(Dz.FragileBones)) __0 *= Plugin.CfgBones.Value;
                if (Plugin.Has(Dz.PainInsensitivity)) __0 *= Plugin.CfgPainBlindDamage.Value;
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }

    // Диабет: еда поднимает сахар (Body.Eat(голод, вес, тип))
    [HarmonyPatch(typeof(Body), "Eat", new Type[] { typeof(float), typeof(float), typeof(int) })]
    static class Patch_BodyEat
    {
        static void Prefix(Body __instance, float __0)
        {
            try { Diseases.OnEat(__instance, __0); }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }

    // Экран смерти: после текста настроения дописываем болезни.
    // Текст собирается в корутине PlayerCamera.EndSequence: ... ToUpper(); set_text(...)
    [HarmonyPatch]
    static class Patch_DeathState
    {
        static MethodBase TargetMethod()
        {
            foreach (Type t in typeof(PlayerCamera).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (t.Name.StartsWith("<EndSequence>"))
                {
                    MethodInfo m = AccessTools.Method(t, "MoveNext");
                    if (m != null) return m;
                }
            }
            return null;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo hook = AccessTools.Method(typeof(Plugin), "DeathState", new Type[] { typeof(string) });
            List<CodeInstruction> list = new List<CodeInstruction>(code);
            int seen = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldstr && list[i].operand as string == "moodrange") { seen = i; break; }
            }
            if (seen < 0) throw new InvalidOperationException("В PlayerCamera.EndSequence не найден текст настроения (версия игры изменилась?)");
            for (int i = seen; i < list.Count; i++)
            {
                MethodInfo mi = list[i].operand as MethodInfo;
                if (list[i].opcode == OpCodes.Callvirt && mi != null && mi.Name == "ToUpper" && mi.GetParameters().Length == 0)
                {
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Call, hook));
                    return list;
                }
            }
            throw new InvalidOperationException("В PlayerCamera.EndSequence после настроения не найден ToUpper (версия игры изменилась?)");
        }
    }
}