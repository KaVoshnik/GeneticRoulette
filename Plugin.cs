using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GeneticRoulette
{
    public enum Dz
    {
        Hiv = 0,
        Hemophilia = 1,
        Asthma = 2,
        Deafness = 3,
        Narcolepsy = 4,
        FragileBones = 5,
        Deuteranopia = 6,
        Achromatopsia = 7
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "geneticroulette.casualtiesunknown";
        public const string PluginName = "Genetic Disease Roulette";
        public const string PluginVersion = "1.0.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // ---------- конфиг ----------
        internal static ConfigEntry<string> CfgLang;
        internal static ConfigEntry<int> CfgRollCount;
        internal static ConfigEntry<float> CfgHealthyChance;
        internal static ConfigEntry<bool> CfgShowRoulette;
        internal static ConfigEntry<KeyCode> CfgListKey;
        internal static ConfigEntry<KeyCode> CfgRerollKey;
        internal static ConfigEntry<string> CfgForce;
        internal static ConfigEntry<bool> CfgSkipTutorial;
        internal static ConfigEntry<string> CfgAccent;
        internal static ConfigEntry<float> CfgUiScale;

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

        // эффекты
        static float asthmaTimer, asthmaLeft;
        static float narcTimer, narcSleepTimer;
        static bool narcTriggered, narcWarned;

        // зрение
        GameObject visionGo;
        float visionCheck;

        readonly RouletteUi ui = new RouletteUi();
        static float spinProgress;

        // рефлексия
        internal static MethodInfo MiBleedGet, MiBleedSet, MiClotGet, MiClotSet, MiNapAlt, MiDropItem;

        static readonly string[] NameRu =
        {
            "ВИЧ (хроническая иммунная недостаточность)",
            "Гемофилия",
            "Астма",
            "Тугоухость",
            "Нарколепсия",
            "Хрупкие кости",
            "Дальтонизм (красно-зелёный)",
            "Ахроматопсия (мир без цвета)"
        };
        static readonly string[] NameEn =
        {
            "HIV (chronic immunodeficiency)",
            "Hemophilia",
            "Asthma",
            "Hearing loss",
            "Narcolepsy",
            "Brittle bones",
            "Red-green color blindness",
            "Achromatopsia (no color)"
        };
        static readonly string[] DescRu =
        {
            "Раны инфицируются заметно быстрее.",
            "Кровотечения сильнее и останавливаются хуже.",
            "Периодические приступы удушья: падают кислород и выносливость.",
            "Постоянная потеря слуха.",
            "Внезапные приступы сна. Предметы из рук выпадают.",
            "Падения и удары ранят и ломают сильнее.",
            "Красный и зелёный сливаются.",
            "Весь мир в оттенках серого."
        };
        static readonly string[] DescEn =
        {
            "Wounds get infected much faster.",
            "Bleeding is heavier and stops worse.",
            "Periodic attacks: oxygen and stamina drop.",
            "Permanent hearing loss.",
            "Sudden sleep attacks. You drop what you hold.",
            "Falls and impacts injure and break more.",
            "Red and green blur together.",
            "The whole world is grayscale."
        };

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
            CfgRerollKey = Config.Bind("Debug", "RerollKey", KeyCode.F8, "Тест: принудительно бросить рулетку заново прямо сейчас");
            CfgForce = Config.Bind("Debug", "ForceDiseases", "", "Для теста: принудительный список через запятую (Hiv,Hemophilia,Asthma,Deafness,Narcolepsy,FragileBones,Deuteranopia,Achromatopsia). Пусто = случайно");
            CfgAccent = Config.Bind("UI", "AccentColor", "#C93C3C", "Цвет рамок и акцентов интерфейса (HTML-цвет)");
            CfgUiScale = Config.Bind("UI", "UiScale", 1.0f, "Масштаб окон мода (0.5-2.5)");
            CfgSkipTutorial = Config.Bind("Debug", "SkipInTutorial", false, "Не бросать рулетку, если в сцене есть TutorialHandler");

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

            MiBleedGet = AccessTools.PropertyGetter(typeof(Body), "bleedingSpeedMultiplier");
            MiBleedSet = AccessTools.PropertySetter(typeof(Body), "bleedingSpeedMultiplier");
            MiClotGet = AccessTools.PropertyGetter(typeof(Body), "bleedClottingSpeed");
            MiClotSet = AccessTools.PropertySetter(typeof(Body), "bleedClottingSpeed");
            MiNapAlt = AccessTools.Method(typeof(Body), "AltNapCoroutine");
            MiDropItem = AccessTools.Method(typeof(Body), "DropItem", new Type[] { typeof(int) });

            try
            {
                GameObject go = new GameObject("GeneticRoulette_Runner");
                go.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(go);
                go.AddComponent<Runner>();
                Log.LogInfo("Runner created");
            }
            catch (Exception e) { Log.LogError("Runner creation failed: " + e); }

            var harmony = new Harmony(PluginGuid);
            int ok = 0, fail = 0;
            foreach (Type t in typeof(Plugin).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try { harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception e) { fail++; Log.LogError("Patch " + t.Name + " failed: " + e); }
            }
            Log.LogInfo(PluginName + " " + PluginVersion + ": patches ok=" + ok + " failed=" + fail);
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
            PlayerCamera cam = PlayerCamera.main;
            return cam != null && b != null && cam.body == b;
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

            NewRoll();
        }

        void NewRoll()
        {
            rolls = DoRoll();
            Active = new HashSet<Dz>();
            foreach (int r in rolls) if (r >= 0) Active.Add((Dz)r);
            SaveSidecar();
            EnsureVision(true);
            Log.LogInfo("New run diseases: " + Describe(Active));

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
            asthmaLeft = 0f;
            asthmaTimer = UnityEngine.Random.Range(CfgAsthmaMin.Value, CfgAsthmaMax.Value) * 0.5f;
            narcTimer = UnityEngine.Random.Range(CfgNarcMin.Value, CfgNarcMax.Value) * 0.5f;
            narcSleepTimer = 0f;
            narcTriggered = false;
            narcWarned = false;
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
                        if (!result.Contains((int)d)) result.Add((int)d);
                    }
                    catch (Exception) { Log.LogWarning("ForceDiseases: unknown name '" + part + "'"); }
                }
                if (result.Count > 0) return result;
            }

            List<int> pool = new List<int>();
            foreach (Dz d in Enum.GetValues(typeof(Dz))) pool.Add((int)d);

            int n = Mathf.Clamp(CfgRollCount.Value, 1, 3);
            for (int i = 0; i < n; i++)
            {
                if (UnityEngine.Random.value < CfgHealthyChance.Value || pool.Count == 0)
                {
                    result.Add(-1);
                    continue;
                }
                int pick = pool[UnityEngine.Random.Range(0, pool.Count)];
                result.Add(pick);
                pool.Remove(pick);
                // два вида нарушения цветового зрения не сочетаем
                if (pick == (int)Dz.Achromatopsia) pool.Remove((int)Dz.Deuteranopia);
                if (pick == (int)Dz.Deuteranopia) pool.Remove((int)Dz.Achromatopsia);
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
                    try { set.Add((Dz)Enum.Parse(typeof(Dz), part.Trim(), true)); } catch (Exception) { }
                }
                return set;
            }
            catch (Exception e) { Log.LogWarning("Could not read sidecar: " + e.Message); return null; }
        }

        // ---------- тексты ----------
        static bool Ru()
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

        static string DName(Dz d) { return Ru() ? NameRu[(int)d] : NameEn[(int)d]; }
        static string DDesc(Dz d) { return Ru() ? DescRu[(int)d] : DescEn[(int)d]; }
        static string Healthy() { return Ru() ? "Здоров" : "Healthy"; }
        static string RollName(int v) { return v < 0 ? Healthy() : DName((Dz)v); }

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

        void HandleKey(KeyCode key)
        {
            if (Time.realtimeSinceStartup - lastKeyTime < 0.25f) return;
            lastKeyTime = Time.realtimeSinceStartup;
            if (key == CfgListKey.Value)
            {
                showList = !showList;
                Log.LogInfo("List toggled: " + showList);
            }
            else if (key == CfgRerollKey.Value)
            {
                Log.LogInfo("Manual reroll");
                ResetEffectState();
                NewRoll();
            }
        }

        internal void Step()
        {
            if (!stepLogged) { stepLogged = true; Log.LogInfo("Update loop is running"); }

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
                    if (Input.GetKeyDown(CfgListKey.Value)) HandleKey(CfgListKey.Value);
                    if (Input.GetKeyDown(CfgRerollKey.Value)) HandleKey(CfgRerollKey.Value);
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
                            reelText = RollName(UnityEngine.Random.Range(-1, 8));
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
                if (e.keyCode == CfgListKey.Value) HandleKey(CfgListKey.Value);
                else if (e.keyCode == CfgRerollKey.Value) HandleKey(CfgRerollKey.Value);
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
                listOn, head, body.ToString(), lines, toastOn, toastText);
        }

        // =====================================================================
        // Зрение: URP Volume с ChannelMixer / ColorAdjustments
        // =====================================================================
        void EnsureVision(bool rebuild)
        {
            bool need = Has(Dz.Deuteranopia) || Has(Dz.Achromatopsia);

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

                if (Has(Dz.Deuteranopia))
                {
                    ChannelMixer cm = prof.Add<ChannelMixer>(true);
                    // матрица Machado et al. 2009 (дейтеранопия), значения в процентах
                    cm.redOutRedIn.Override(36.7f);
                    cm.redOutGreenIn.Override(86.1f);
                    cm.redOutBlueIn.Override(-22.8f);
                    cm.greenOutRedIn.Override(28.0f);
                    cm.greenOutGreenIn.Override(67.3f);
                    cm.greenOutBlueIn.Override(4.7f);
                    cm.blueOutRedIn.Override(-1.2f);
                    cm.blueOutGreenIn.Override(4.3f);
                    cm.blueOutBlueIn.Override(96.9f);
                }
                if (Has(Dz.Achromatopsia))
                {
                    ColorAdjustments ca = prof.Add<ColorAdjustments>(true);
                    ca.saturation.Override(-100f);
                }

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
            float dt = Time.deltaTime;

            // --- глухота ---
            if (Has(Dz.Deafness) && b.hearingLoss < CfgDeafFloor.Value)
                b.hearingLoss = CfgDeafFloor.Value;

            // --- астма ---
            if (Has(Dz.Asthma))
            {
                if (asthmaLeft > 0f)
                {
                    asthmaLeft -= dt;
                    float floor = CfgAsthmaFloor.Value;
                    if (b.bloodOxygen > floor)
                        b.bloodOxygen = Mathf.Max(floor, b.bloodOxygen - dt * 2.2f);
                    b.stamina = Mathf.Max(0f, b.stamina - dt * 6f);
                    if (asthmaLeft <= 0f)
                        Toast(Ru() ? "Приступ астмы проходит." : "The asthma attack subsides.", 3f);
                }
                else
                {
                    asthmaTimer -= dt;
                    if (asthmaTimer <= 0f && b.conscious && !b.sleeping)
                    {
                        asthmaLeft = UnityEngine.Random.Range(25f, 45f);
                        asthmaTimer = UnityEngine.Random.Range(CfgAsthmaMin.Value, CfgAsthmaMax.Value);
                        Toast(Ru() ? "Приступ астмы! Вам нечем дышать." : "Asthma attack! You can't breathe.", 4f);
                    }
                }
            }

            // --- нарколепсия ---
            if (Has(Dz.Narcolepsy))
            {
                if (b.sleeping)
                {
                    if (narcTriggered && CfgNarcSleep.Value > 0f)
                    {
                        narcSleepTimer += dt;
                        if (narcSleepTimer >= CfgNarcSleep.Value)
                        {
                            narcTriggered = false;
                            try { b.WakeUp(); } catch (Exception e) { Log.LogWarning("WakeUp failed: " + e.Message); }
                        }
                    }
                }
                else if (b.conscious)
                {
                    narcTriggered = false;
                    narcTimer -= dt;
                    if (narcTimer <= 4f && !narcWarned)
                    {
                        narcWarned = true;
                        Toast(Ru() ? "Вас неудержимо клонит в сон…" : "You feel an overwhelming urge to sleep…", 4f);
                    }
                    if (narcTimer <= 0f)
                    {
                        narcTimer = UnityEngine.Random.Range(CfgNarcMin.Value, CfgNarcMax.Value);
                        narcWarned = false;
                        TriggerNarcolepsy(b);
                    }
                }
            }
        }

        static void TriggerNarcolepsy(Body b)
        {
            try
            {
                if (MiDropItem != null)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try { MiDropItem.Invoke(b, new object[] { i }); } catch (Exception) { }
                    }
                }
                if (MiNapAlt != null)
                {
                    IEnumerator routine = (IEnumerator)MiNapAlt.Invoke(b, null);
                    b.StartCoroutine(routine);
                    narcTriggered = true;
                    narcSleepTimer = 0f;
                }
            }
            catch (Exception e) { Log.LogError("Narcolepsy failed: " + e); }
        }

        // Гемофилия: вызывается после Body.HandlePeriodicChecks (там значения задаются заново)
        internal static void ApplyHemophilia(Body b)
        {
            if (!Has(Dz.Hemophilia)) return;
            try
            {
                if (MiBleedGet != null && MiBleedSet != null)
                {
                    float v = (float)MiBleedGet.Invoke(b, null);
                    MiBleedSet.Invoke(b, new object[] { v * CfgHemoBleed.Value });
                }
                if (MiClotGet != null && MiClotSet != null)
                {
                    float c = (float)MiClotGet.Invoke(b, null);
                    MiClotSet.Invoke(b, new object[] { c * CfgHemoClot.Value });
                }
            }
            catch (Exception e) { Log.LogError("Hemophilia failed: " + e); }
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
            try { if (Plugin.IsPlayer(__instance)) Plugin.ApplyHemophilia(__instance); }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }

    // ВИЧ: множитель шанса заражения читается в Limb.Update, подменяем на время вызова
    [HarmonyPatch(typeof(Limb), "Update")]
    static class Patch_LimbUpdate
    {
        static void Prefix(Limb __instance, ref float __state)
        {
            __state = -1f;
            try
            {
                if (!Plugin.Has(Dz.Hiv)) return;
                Body b = __instance.body;
                if (!Plugin.IsPlayer(b)) return;
                __state = b.infectionChanceMultiplier;
                b.infectionChanceMultiplier = __state * Plugin.CfgHivInfection.Value;
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }

        static void Finalizer(Limb __instance, float __state)
        {
            if (__state < 0f) return;
            try { __instance.body.infectionChanceMultiplier = __state; }
            catch (Exception) { }
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
                if (!Plugin.Has(Dz.FragileBones)) return;
                if (!Plugin.IsPlayer(__instance.body)) return;
                __0 *= Plugin.CfgBones.Value;
            }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }
    }
}
