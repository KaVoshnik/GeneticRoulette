using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GeneticRoulette
{
    /// <summary>
    /// Идентификаторы болезней. В файле сохранения хранятся ИМЕНА, а не числа,
    /// поэтому порядок можно менять, а новые значения добавлять в конец.
    /// </summary>
    public enum Dz
    {
        Hiv = 0,
        Hemophilia = 1,
        Asthma = 2,
        Deafness = 3,
        Narcolepsy = 4,
        FragileBones = 5,
        Deuteranopia = 6,
        Achromatopsia = 7,
        Protanopia = 8,
        Tritanopia = 9,
        Anemia = 10,
        Diabetes = 11,
        Epilepsy = 12,
        PainInsensitivity = 13
    }

    /// <summary>Описание статуса (мудлета) болезни в панели игры.</summary>
    internal sealed class MoodleSpec
    {
        public int Bg;
        public string Name;
        public string Desc;
        public bool Flash;
        public string[] Icons;
    }

    /// <summary>
    /// Одна болезнь целиком: тексты, вес в рулетке, несовместимости и все хуки эффектов.
    /// Чтобы добавить новую болезнь: значение в enum Dz + один блок в Diseases.Build().
    /// </summary>
    internal sealed class DiseaseDef
    {
        public Dz Id;
        public string NameRu, NameEn, DescRu, DescEn;

        /// <summary>Короткое название (для экрана смерти и баннера в меню здоровья).</summary>
        public string ShortRu, ShortEn;

        /// <summary>Относительный шанс выпадения (1 = обычный).</summary>
        public float Weight = 1f;

        /// <summary>Болезни, которые не могут выпасть вместе с этой.</summary>
        public Dz[] Incompatible = new Dz[0];

        /// <summary>Каждый кадр на теле игрока.</summary>
        public Action<Body> Tick;

        /// <summary>После Body.HandlePeriodicChecks (вызывается каждый кадр!).</summary>
        public Action<Body> Periodic;

        /// <summary>Сброс состояния эффекта в начале забега.</summary>
        public Action Reset;

        /// <summary>Статус в панели; параметр — русский ли язык. null = без статуса.</summary>
        public Func<bool, MoodleSpec> Moodle;

        /// <summary>Вклад в URP-профиль цветового зрения. null = болезнь не трогает экран.</summary>
        public Action<VolumeProfile> Vision;

        /// <summary>Небольшой плюс болезни (баланс). null = плюса нет. Работает при Tuning.EnablePerks.</summary>
        public string PerkRu, PerkEn;

        public string Name(bool ru) { return ru ? NameRu : NameEn; }
        public string Short(bool ru) { string s = ru ? ShortRu : ShortEn; return string.IsNullOrEmpty(s) ? Name(ru) : s; }
        public string Desc(bool ru) { return ru ? DescRu : DescEn; }

        /// <summary>Текст плюса или null, если плюсов нет либо они выключены в конфиге.</summary>
        public string Perk(bool ru)
        {
            if (!Plugin.CfgPerks.Value) return null;
            return ru ? PerkRu : PerkEn;
        }
    }

    internal static class Diseases
    {
        internal static readonly List<DiseaseDef> All = Build();

        internal static DiseaseDef Get(Dz id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        static string Num(float v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        // ---------- состояние лечения (заполняется лекарствами из CustomItems) ----------
        /// <summary>Секунд защиты от приступов астмы после ингалятора.</summary>
        internal static float AsthmaProtect;
        /// <summary>Секунд подавления ВИЧ после антиретровирусного препарата.</summary>
        internal static float ArvTime;
        /// <summary>Секунд действия инсулина.</summary>
        internal static float InsulinTime;
        /// <summary>Уровень сахара в крови (норма около 45, высокий больше 75, низкий меньше 30).</summary>
        internal static float Sugar = 45f;

        static float tremorTimer;
        static float epiTimer;
        static bool epiWarned;

        // ---------- состояние эффектов ----------
        static float asthmaTimer, asthmaLeft;
        static float narcTimer, narcSleepTimer;
        static bool narcTriggered, narcWarned;

        internal static bool AsthmaAttack { get { return asthmaLeft > 0f; } }
        internal static bool NarcWarning { get { return narcWarned && !narcTriggered; } }

        // ---------- рефлексия (заполняется в Init) ----------
        static MethodInfo miNapAlt, miDropItem;
        static AccessTools.FieldRef<Body, float> bleedRef, clotRef;
        static Body hemoBody;
        static float lastBleedOut = float.NaN, lastClotOut = float.NaN;

        /// <summary>Проверка и кеширование всего, что берётся из игры рефлексией.</summary>
        internal static void Init()
        {
            miNapAlt = AccessTools.Method(typeof(Body), "AltNapCoroutine");
            miDropItem = AccessTools.Method(typeof(Body), "DropItem", new Type[] { typeof(int) });
            try { bleedRef = AccessTools.FieldRefAccess<Body, float>("<bleedingSpeedMultiplier>k__BackingField"); } catch (Exception) { bleedRef = null; }
            try { clotRef = AccessTools.FieldRefAccess<Body, float>("<bleedClottingSpeed>k__BackingField"); } catch (Exception) { clotRef = null; }

            if (miNapAlt == null) Plugin.Problem("Body.AltNapCoroutine не найден: нарколепсия не будет работать");
            if (miDropItem == null) Plugin.Problem("Body.DropItem(int) не найден: при нарколепсии предметы не будут выпадать");
            if (bleedRef == null || clotRef == null) Plugin.Problem("Поля кровотечения Body не найдены: гемофилия не будет работать");
        }

        internal static void ResetAll()
        {
            hemoBody = null;
            lastBleedOut = lastClotOut = float.NaN;
            AsthmaProtect = ArvTime = InsulinTime = 0f;
            Sugar = 45f;
            foreach (DiseaseDef d in All) if (d.Reset != null) d.Reset();
        }

        // =====================================================================
        // Определения болезней
        // =====================================================================
        static List<DiseaseDef> Build()
        {
            List<DiseaseDef> l = new List<DiseaseDef>();

            l.Add(new DiseaseDef
            {
                Id = Dz.Hiv,
                ShortRu = "ВИЧ",
                ShortEn = "HIV",
                NameRu = "ВИЧ (хроническая иммунная недостаточность)",
                NameEn = "HIV (chronic immunodeficiency)",
                DescRu = "Раны инфицируются заметно быстрее.",
                DescEn = "Wounds get infected much faster.",
                // сам эффект — транспилер Limb.Update, см. Patch_LimbUpdate / InfectionMultiplier
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "ВИЧ" : "HIV",
                    Desc = ArvTime > 0f
                        ? (ru ? "Хроническая иммунная недостаточность. Терапия действует, шанс заражения ран обычный (ещё " + Mathf.CeilToInt(ArvTime) + " с)."
                              : "Chronic immunodeficiency. Therapy is active, wound infection chance is normal (" + Mathf.CeilToInt(ArvTime) + " s left).")
                        : (ru ? "Хроническая иммунная недостаточность. Шанс заражения ран ×" + Num(Plugin.CfgHivInfection.Value) + ". Помогает антиретровирусный препарат."
                              : "Chronic immunodeficiency. Wound infection chance x" + Num(Plugin.CfgHivInfection.Value) + ". Antiretroviral drug helps."),
                    Icons = new[] { "lowimmunity", "highimmunity", "infected" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Hemophilia,
                ShortRu = "Гемофилия",
                ShortEn = "Hemophilia",
                NameRu = "Гемофилия",
                NameEn = "Hemophilia",
                DescRu = "Кровотечения сильнее и останавливаются хуже.",
                DescEn = "Bleeding is heavier and stops worse.",
                PerkRu = "Тромбов не бывает: лёгочная эмболия вам не грозит.",
                PerkEn = "No blood clots: pulmonary embolism can't happen to you.",
                Periodic = ApplyHemophilia,
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "Гемофилия" : "Hemophilia",
                    Desc = ru ? "Кровь плохо свёртывается: кровотечения сильнее и останавливаются дольше."
                              : "Blood barely clots: bleeding is heavier and lasts longer.",
                    Icons = new[] { "heavybleeding", "bleeding", "smallbleeding" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Asthma,
                ShortRu = "Астма",
                ShortEn = "Asthma",
                NameRu = "Астма",
                NameEn = "Asthma",
                DescRu = "Периодические приступы удушья: падают кислород и выносливость.",
                DescEn = "Periodic attacks: oxygen and stamina drop.",
                Tick = AsthmaTick,
                Reset = () => { asthmaLeft = 0f; asthmaTimer = UnityEngine.Random.Range(Plugin.CfgAsthmaMin.Value, Plugin.CfgAsthmaMax.Value) * 0.5f; },
                Moodle = ru => AsthmaAttack
                    ? new MoodleSpec
                    {
                        Bg = 2, Flash = true,
                        Name = ru ? "Приступ астмы" : "Asthma attack",
                        Desc = ru ? "Не хватает воздуха: падают кислород и выносливость."
                                  : "You can't get enough air: oxygen and stamina drop.",
                        Icons = new[] { "cantbreathe", "hypoventilation", "oxygen" }
                    }
                    : new MoodleSpec
                    {
                        Bg = 1,
                        Name = ru ? "Астма" : "Asthma",
                        Desc = AsthmaProtect > 0f
                            ? (ru ? "Ингалятор действует: приступов нет (ещё " + Mathf.CeilToInt(AsthmaProtect) + " с)."
                                  : "Inhaler is active: no attacks (" + Mathf.CeilToInt(AsthmaProtect) + " s left).")
                            : (ru ? "Время от времени накатывают приступы удушья. Помогает ингалятор."
                                  : "Sudden attacks of suffocation come from time to time. An inhaler helps."),
                        Icons = new[] { "hypoventilation", "cantbreathe", "oxygen" }
                    }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Deafness,
                ShortRu = "Тугоухость",
                ShortEn = "Hearing loss",
                NameRu = "Тугоухость",
                NameEn = "Hearing loss",
                DescRu = "Постоянная потеря слуха.",
                DescEn = "Permanent hearing loss.",
                PerkRu = "Привыкли к тишине: постоянная тугоухость не портит настроение.",
                PerkEn = "Used to the quiet: the permanent hearing loss doesn't hurt your mood.",
                Tick = b => { if (b.hearingLoss < Plugin.CfgDeafFloor.Value) b.hearingLoss = Plugin.CfgDeafFloor.Value; },
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "Тугоухость" : "Hearing loss",
                    Desc = ru ? "Постоянная потеря слуха." : "Permanent hearing loss.",
                    Icons = new[] { "hearingloss" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Narcolepsy,
                ShortRu = "Нарколепсия",
                ShortEn = "Narcolepsy",
                NameRu = "Нарколепсия",
                NameEn = "Narcolepsy",
                DescRu = "Внезапные приступы сна. Предметы из рук выпадают.",
                DescEn = "Sudden sleep attacks. You drop what you hold.",
                Tick = NarcolepsyTick,
                Reset = () =>
                {
                    narcTimer = UnityEngine.Random.Range(Plugin.CfgNarcMin.Value, Plugin.CfgNarcMax.Value) * 0.5f;
                    narcSleepTimer = 0f; narcTriggered = false; narcWarned = false;
                },
                Moodle = ru => NarcWarning
                    ? new MoodleSpec
                    {
                        Bg = 2, Flash = true,
                        Name = ru ? "Клонит в сон" : "Drifting off",
                        Desc = ru ? "Вы вот-вот отключитесь. Найдите безопасное место."
                                  : "You are about to pass out. Find a safe spot.",
                        Icons = new[] { "tired", "asleep", "badsleep" }
                    }
                    : new MoodleSpec
                    {
                        Bg = 1,
                        Name = ru ? "Нарколепсия" : "Narcolepsy",
                        Desc = ru ? "Внезапные приступы сна. Предметы из рук выпадают."
                                  : "Sudden sleep attacks. You drop what you hold.",
                        Icons = new[] { "badsleep", "tired", "asleep" }
                    }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.FragileBones,
                ShortRu = "Хрупкие кости",
                ShortEn = "Brittle bones",
                NameRu = "Хрупкие кости",
                NameEn = "Brittle bones",
                DescRu = "Падения и удары ранят и ломают сильнее.",
                DescEn = "Falls and impacts injure and break more.",
                // сам эффект — Patch_LimbImpact
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "Хрупкие кости" : "Brittle bones",
                    Desc = ru ? "Падения и удары ранят и ломают сильнее (×" + Num(Plugin.CfgBones.Value) + ")."
                              : "Falls and impacts injure and break more (x" + Num(Plugin.CfgBones.Value) + ").",
                    Icons = new[] { "brokenbone", "dislocation" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Deuteranopia,
                ShortRu = "Дейтеранопия",
                ShortEn = "Deuteranopia",
                NameRu = "Дейтеранопия (красно-зелёный дальтонизм)",
                NameEn = "Deuteranopia (red-green color blindness)",
                DescRu = "Красный и зелёный сливаются.",
                DescEn = "Red and green blur together.",
                Incompatible = new[] { Dz.Achromatopsia, Dz.Protanopia, Dz.Tritanopia },
                Vision = prof =>
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
                },
                Moodle = ru => new MoodleSpec
                {
                    Bg = 0,
                    Name = ru ? "Дальтонизм" : "Color blindness",
                    Desc = ru ? "Красный и зелёный сливаются." : "Red and green blur together.",
                    Icons = new[] { "rippedeyes", "rippedeye", "confused", "focused" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Achromatopsia,
                ShortRu = "Ахроматопсия",
                ShortEn = "Achromatopsia",
                NameRu = "Ахроматопсия (мир без цвета)",
                NameEn = "Achromatopsia (no color)",
                DescRu = "Весь мир в оттенках серого.",
                DescEn = "The whole world is grayscale.",
                Incompatible = new[] { Dz.Deuteranopia, Dz.Protanopia, Dz.Tritanopia },
                Vision = prof =>
                {
                    ColorAdjustments ca = prof.Add<ColorAdjustments>(true);
                    ca.saturation.Override(-100f);
                },
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "Ахроматопсия" : "Achromatopsia",
                    Desc = ru ? "Весь мир в оттенках серого." : "The whole world is grayscale.",
                    Icons = new[] { "rippedeyes", "rippedeye", "confused", "focused" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Protanopia,
                ShortRu = "Протанопия", ShortEn = "Protanopia",
                NameRu = "Протанопия (нет красного)",
                NameEn = "Protanopia (no red cones)",
                DescRu = "Красный цвет тускнеет и сливается с зелёным.",
                DescEn = "Reds look dim and blend with greens.",
                Incompatible = new[] { Dz.Deuteranopia, Dz.Achromatopsia, Dz.Tritanopia },
                Vision = prof =>
                {
                    ChannelMixer cm = prof.Add<ChannelMixer>(true);
                    // матрица Machado et al. 2009 (протанопия), значения в процентах
                    cm.redOutRedIn.Override(15.2f);
                    cm.redOutGreenIn.Override(105.3f);
                    cm.redOutBlueIn.Override(-20.5f);
                    cm.greenOutRedIn.Override(11.5f);
                    cm.greenOutGreenIn.Override(78.6f);
                    cm.greenOutBlueIn.Override(9.9f);
                    cm.blueOutRedIn.Override(-0.4f);
                    cm.blueOutGreenIn.Override(-4.8f);
                    cm.blueOutBlueIn.Override(105.2f);
                },
                Moodle = ru => new MoodleSpec
                {
                    Bg = 0,
                    Name = ru ? "Протанопия" : "Protanopia",
                    Desc = ru ? "Красный тускнеет и сливается с зелёным." : "Reds look dim and blend with greens.",
                    Icons = new[] { "rippedeyes", "rippedeye", "confused", "focused" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Tritanopia,
                ShortRu = "Тританопия", ShortEn = "Tritanopia",
                NameRu = "Тританопия (сине-жёлтый дальтонизм)",
                NameEn = "Tritanopia (blue-yellow color blindness)",
                DescRu = "Синий и зелёный путаются, жёлтый кажется розоватым.",
                DescEn = "Blues and greens get mixed up, yellows look pinkish.",
                Weight = 0.6f,
                Incompatible = new[] { Dz.Deuteranopia, Dz.Achromatopsia, Dz.Protanopia },
                Vision = prof =>
                {
                    ChannelMixer cm = prof.Add<ChannelMixer>(true);
                    // матрица Machado et al. 2009 (тританопия), значения в процентах
                    cm.redOutRedIn.Override(125.6f);
                    cm.redOutGreenIn.Override(-7.7f);
                    cm.redOutBlueIn.Override(-17.9f);
                    cm.greenOutRedIn.Override(-7.8f);
                    cm.greenOutGreenIn.Override(93.1f);
                    cm.greenOutBlueIn.Override(14.8f);
                    cm.blueOutRedIn.Override(0.5f);
                    cm.blueOutGreenIn.Override(69.1f);
                    cm.blueOutBlueIn.Override(30.4f);
                },
                Moodle = ru => new MoodleSpec
                {
                    Bg = 0,
                    Name = ru ? "Тританопия" : "Tritanopia",
                    Desc = ru ? "Синий и зелёный путаются." : "Blues and greens get mixed up.",
                    Icons = new[] { "rippedeyes", "rippedeye", "confused", "focused" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Anemia,
                ShortRu = "Анемия", ShortEn = "Anemia",
                NameRu = "Анемия",
                NameEn = "Anemia",
                DescRu = "Крови в организме меньше нормы, она не восстанавливается выше порога.",
                DescEn = "You carry less blood than normal and it never regenerates past a cap.",
                Tick = b =>
                {
                    float cap = Plugin.CfgAnemiaCap.Value;
                    if (b.bloodVolume > cap) b.bloodVolume = cap;
                },
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "Анемия" : "Anemia",
                    Desc = ru ? "Объём крови не поднимается выше " + Num(Plugin.CfgAnemiaCap.Value) + "%. Любая кровопотеря опаснее."
                              : "Blood volume never rises above " + Num(Plugin.CfgAnemiaCap.Value) + "%. Any blood loss is more dangerous.",
                    Icons = new[] { "hypotension", "bleeding", "tired", "oxygen" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Diabetes,
                ShortRu = "Диабет", ShortEn = "Diabetes",
                NameRu = "Диабет 1 типа",
                NameEn = "Type 1 diabetes",
                DescRu = "Сахар в крови растёт сам и от еды. Нужен инсулин: без него жажда, слабость и тошнота, передозировка вызывает гипогликемию.",
                DescEn = "Blood sugar climbs on its own and from food. You need insulin: without it, thirst, weakness and nausea; too much causes hypoglycemia.",
                Tick = DiabetesTick,
                Reset = () => { Sugar = 45f; InsulinTime = 0f; tremorTimer = 0f; },
                Moodle = ru => DiabetesMoodle(ru)
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Epilepsy,
                ShortRu = "Эпилепсия", ShortEn = "Epilepsy",
                NameRu = "Эпилепсия",
                NameEn = "Epilepsy",
                DescRu = "Периодические припадки: тряска экрана, инвертированное управление, затем падение.",
                DescEn = "Periodic seizures: screen shaking, reversed controls, then you collapse.",
                Tick = EpilepsyTick,
                Reset = () => { epiTimer = UnityEngine.Random.Range(Plugin.CfgEpiMin.Value, Plugin.CfgEpiMax.Value) * 0.5f; epiWarned = false; },
                Moodle = ru => epiWarned
                    ? new MoodleSpec
                    {
                        Bg = 2, Flash = true,
                        Name = ru ? "Аура" : "Aura",
                        Desc = ru ? "Предприпадочное состояние. Сядьте или отойдите от опасных мест."
                                  : "Pre-seizure aura. Get to a safe spot.",
                        Icons = new[] { "confused", "concussion", "palpitations" }
                    }
                    : new MoodleSpec
                    {
                        Bg = 1,
                        Name = ru ? "Эпилепсия" : "Epilepsy",
                        Desc = ru ? "Время от времени случаются припадки." : "Seizures come from time to time.",
                        Icons = new[] { "concussion", "confused", "braindamage" }
                    }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.PainInsensitivity,
                ShortRu = "Нечувствительность к боли", ShortEn = "Pain insensitivity",
                NameRu = "Врождённая нечувствительность к боли",
                NameEn = "Congenital insensitivity to pain",
                DescRu = "Боль почти не ощущается, но удары и падения бьют сильнее: тело не предупреждает об опасности.",
                DescEn = "Pain is barely felt, but impacts hurt your body more: nothing warns you of danger.",
                PerkRu = "Боль и болевой шок вам почти не мешают.",
                PerkEn = "Pain and pain shock barely affect you.",
                Tick = b =>
                {
                    float cap = Plugin.CfgPainCap.Value;
                    Limb[] limbs = b.limbs;
                    if (limbs == null) return;
                    for (int i = 0; i < limbs.Length; i++)
                    {
                        Limb lb = limbs[i];
                        if (lb != null && lb.pain > cap) lb.pain = cap;
                    }
                },
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "Не чувствуете боли" : "Numb to pain",
                    Desc = ru ? "Боль не ощущается, но удары и падения бьют сильнее (×" + Num(Plugin.CfgPainBlindDamage.Value) + ")."
                              : "You feel no pain, but impacts hurt more (x" + Num(Plugin.CfgPainBlindDamage.Value) + ").",
                    Icons = new[] { "stimulated", "adrenaline", "shock", "pain" }
                }
            });

            return l;
        }

        // =====================================================================
        // Эффекты
        // =====================================================================
        static void AsthmaTick(Body b)
        {
            float dt = Time.deltaTime;
            if (AsthmaProtect > 0f)
            {
                // ингалятор: приступ прекращается и новые не начинаются
                AsthmaProtect -= dt;
                asthmaLeft = 0f;
                return;
            }
            if (asthmaLeft > 0f)
            {
                asthmaLeft -= dt;
                float floor = Plugin.CfgAsthmaFloor.Value;
                if (b.bloodOxygen > floor)
                    b.bloodOxygen = Mathf.Max(floor, b.bloodOxygen - dt * 2.2f);
                b.stamina = Mathf.Max(0f, b.stamina - dt * 6f);
                if (asthmaLeft <= 0f)
                    Plugin.Toast(Plugin.Ru() ? "Приступ астмы проходит." : "The asthma attack subsides.", 3f);
            }
            else
            {
                asthmaTimer -= dt;
                if (asthmaTimer <= 0f && b.conscious && !b.sleeping)
                {
                    asthmaLeft = UnityEngine.Random.Range(25f, 45f);
                    asthmaTimer = UnityEngine.Random.Range(Plugin.CfgAsthmaMin.Value, Plugin.CfgAsthmaMax.Value);
                    Plugin.Toast(Plugin.Ru() ? "Приступ астмы! Вам нечем дышать." : "Asthma attack! You can't breathe.", 4f);
                }
            }
        }

        static void NarcolepsyTick(Body b)
        {
            float dt = Time.deltaTime;
            if (b.sleeping)
            {
                if (narcTriggered && Plugin.CfgNarcSleep.Value > 0f)
                {
                    narcSleepTimer += dt;
                    if (narcSleepTimer >= Plugin.CfgNarcSleep.Value)
                    {
                        narcTriggered = false;
                        try { b.WakeUp(); } catch (Exception e) { Plugin.Log.LogWarning("WakeUp failed: " + e.Message); }
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
                    Plugin.Toast(Plugin.Ru() ? "Вас неудержимо клонит в сон…" : "You feel an overwhelming urge to sleep…", 4f);
                }
                if (narcTimer <= 0f)
                {
                    narcTimer = UnityEngine.Random.Range(Plugin.CfgNarcMin.Value, Plugin.CfgNarcMax.Value);
                    narcWarned = false;
                    TriggerNarcolepsy(b);
                }
            }
        }

        static void TriggerNarcolepsy(Body b)
        {
            try
            {
                if (miDropItem != null)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try { miDropItem.Invoke(b, new object[] { i }); } catch (Exception) { }
                    }
                }
                if (miNapAlt != null)
                {
                    IEnumerator routine = (IEnumerator)miNapAlt.Invoke(b, null);
                    b.StartCoroutine(routine);
                    narcTriggered = true;
                    narcSleepTimer = 0f;
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Narcolepsy failed: " + e); }
        }

        /// <summary>
        /// Гемофилия. Body.HandlePeriodicChecks вызывается КАЖДЫЙ кадр (из Body.Update), но игра
        /// присваивает bleedingSpeedMultiplier и bleedClottingSpeed только раз в секунду.
        /// Поэтому умножаем лишь тогда, когда значение было заново записано игрой, то есть
        /// отличается от того, что оставили мы. Иначе множитель накапливался бы каждый кадр.
        /// </summary>
        static void ApplyHemophilia(Body b)
        {
            if (bleedRef == null || clotRef == null) return;
            if (!ReferenceEquals(b, hemoBody))
            {
                hemoBody = b;
                lastBleedOut = lastClotOut = float.NaN;
            }

            ref float bleed = ref bleedRef(b);
            if (!(bleed == lastBleedOut))
            {
                bleed *= Plugin.CfgHemoBleed.Value;
                lastBleedOut = bleed;
            }

            ref float clot = ref clotRef(b);
            if (!(clot == lastClotOut))
            {
                clot *= Plugin.CfgHemoClot.Value;
                lastClotOut = clot;
            }
        }


        // =====================================================================
        // Диабет
        // =====================================================================
        static void DiabetesTick(Body b)
        {
            float dt = Time.deltaTime;

            if (InsulinTime > 0f)
            {
                InsulinTime -= dt;
                Sugar = Mathf.MoveTowards(Sugar, 20f, dt * Plugin.CfgInsulinPower.Value);
            }
            else
            {
                Sugar = Mathf.MoveTowards(Sugar, 92f, dt * Plugin.CfgSugarRise.Value);
            }
            Sugar = Mathf.Clamp(Sugar, 0f, 130f);

            if (Sugar > 75f)
            {
                // гипергликемия: жажда, слабость, дурное настроение, при высоких значениях тошнота
                float k = Mathf.InverseLerp(75f, 110f, Sugar);
                b.thirst -= dt * (0.05f + 0.15f * k);
                b.stamina = Mathf.Max(0f, b.stamina - dt * 0.8f * k);
                b.happiness -= dt * 0.15f * k;
                if (Sugar > 95f) b.sicknessAmount += dt * 0.2f;
            }
            else if (Sugar < 30f)
            {
                // гипогликемия: голод, дрожь, слабость, при очень низких значениях потеря сознания
                float k = Mathf.InverseLerp(30f, 10f, Sugar);
                b.hunger -= dt * (0.1f + 0.3f * k);
                b.stamina = Mathf.Max(0f, b.stamina - dt * (1f + 2f * k));
                b.happiness -= dt * 0.3f * k;
                tremorTimer -= dt;
                if (tremorTimer <= 0f)
                {
                    tremorTimer = 1.5f;
                    try { PlayerCamera.main.shaker.Shake(1.5f + 3f * k); } catch (Exception) { }
                }
                if (Sugar < 12f) b.consciousness = Mathf.Max(0f, b.consciousness - dt * 10f);
            }
        }

        static MoodleSpec DiabetesMoodle(bool ru)
        {
            string num = Plugin.DebugMode ? " [" + Mathf.RoundToInt(Sugar) + "]" : "";
            string ins = InsulinTime > 0f ? (ru ? " Инсулин действует." : " Insulin is active.") : "";
            if (Sugar > 75f)
                return new MoodleSpec
                {
                    Bg = 2, Flash = true,
                    Name = ru ? "Высокий сахар" : "High blood sugar",
                    Desc = (ru ? "Гипергликемия: жажда, слабость, тошнота. Нужен инсулин." : "Hyperglycemia: thirst, weakness, nausea. You need insulin.") + ins + num,
                    Icons = new[] { "thirst", "sick", "thirst1" }
                };
            if (Sugar < 30f)
                return new MoodleSpec
                {
                    Bg = 2, Flash = true,
                    Name = ru ? "Низкий сахар" : "Low blood sugar",
                    Desc = (ru ? "Гипогликемия: дрожь, слабость, можно потерять сознание. Срочно поешьте." : "Hypoglycemia: shaking, weakness, you may pass out. Eat now.") + ins + num,
                    Icons = new[] { "confused", "tired", "hunger" }
                };
            return new MoodleSpec
            {
                Bg = 1,
                Name = ru ? "Диабет" : "Diabetes",
                Desc = (ru ? "Следите за едой и инсулином." : "Watch what you eat and your insulin.") + ins + num,
                Icons = new[] { "hunger", "thirst", "lowimmunity" }
            };
        }

        /// <summary>Вызывается из Body.Eat (патч в Plugin.cs): еда поднимает сахар.</summary>
        internal static void OnEat(Body b, float hunger)
        {
            if (!Plugin.Has(Dz.Diabetes) || !Plugin.IsPlayer(b)) return;
            Sugar += Mathf.Max(0f, hunger) * Plugin.CfgSugarPerFood.Value;
            // при очень высоком сахаре еда плохо усваивается и тошнит
            if (Sugar > 85f) b.sicknessAmount += 2f;
        }

        /// <summary>Сладкие напитки поднимают сахар (оборачиваем onDrink жидкостей, см. CustomItems).</summary>
        internal static void OnSugaryDrink(Body b, float amount)
        {
            if (!Plugin.Has(Dz.Diabetes) || !Plugin.IsPlayer(b)) return;
            Sugar += Mathf.Max(0f, amount) * Plugin.CfgSugarPerDrink.Value;
        }

        // =====================================================================
        // Эпилепсия
        // =====================================================================
        static void EpilepsyTick(Body b)
        {
            if (b.sleeping || !b.conscious) return;
            float dt = Time.deltaTime;
            epiTimer -= dt;
            if (epiTimer <= 4f && !epiWarned)
            {
                epiWarned = true;
                Plugin.Toast(Plugin.Ru() ? "Перед глазами всё мерцает… Скоро припадок." : "Your vision flickers… A seizure is coming.", 4f);
                try { PlayerCamera.main.shaker.Shake(4f); } catch (Exception) { }
            }
            if (epiTimer <= 0f)
            {
                if (b.inWater) { epiTimer = 8f; return; }   // в воде не бросаем: слишком жестоко
                epiTimer = UnityEngine.Random.Range(Plugin.CfgEpiMin.Value, Plugin.CfgEpiMax.Value);
                epiWarned = false;
                TriggerSeizure(b);
            }
        }

        /// <summary>Припадок собирается из родных «мозговых» эффектов игры (как при повреждении мозга).</summary>
        static void TriggerSeizure(Body b)
        {
            try
            {
                string[] names = { "BrainQuake", "BrainDamageRagdoll", "BrainControlReverse" };
                int started = 0;
                foreach (string n in names)
                {
                    MethodInfo mi = AccessTools.Method(typeof(Body), n);
                    if (mi == null) { Plugin.Log.LogWarning("Seizure: метод Body." + n + " не найден"); continue; }
                    b.StartCoroutine((IEnumerator)mi.Invoke(b, null));
                    started++;
                }
                if (started > 0)
                    Plugin.Toast(Plugin.Ru() ? "Припадок!" : "Seizure!", 4f);
            }
            catch (Exception e) { Plugin.Log.LogError("Seizure failed: " + e); }
        }

        /// <summary>
        /// Плюс гемофилии. HandlePeriodicChecks пересчитывает bloodViscosity и тут же проверяет
        /// «вязкость > 90 → шанс лёгочной эмболии». Транспилер подменяет именно это чтение:
        /// у гемофилика с плюсом проверка видит значение не выше порога, и эмболии не будет.
        /// </summary>
        public static float EmbolismViscosity(Body b)
        {
            float v = b.bloodViscosity;
            if (v > 89f && Plugin.CfgPerks.Value && Plugin.Has(Dz.Hemophilia) && Plugin.IsPlayer(b)) return 89f;
            return v;
        }

        /// <summary>
        /// Плюс тугоухости. Body.totalHappiness вычитает hearingLoss * 0.2. Возвращаем часть,
        /// созданную самим модом (постоянный минимум), так что только дополнительный урон
        /// от громких звуков портит настроение.
        /// </summary>
        public static float DeafnessHappinessBonus(Body b)
        {
            if (!Plugin.CfgPerks.Value || !Plugin.Has(Dz.Deafness) || !Plugin.IsPlayer(b)) return 0f;
            return Mathf.Min(b.hearingLoss, Plugin.CfgDeafFloor.Value) * 0.2f;
        }

        /// <summary>Подставляется транспилером вместо чтения Body.infectionChanceMultiplier в Limb.Update.</summary>
        public static float InfectionMultiplier(Body b)
        {
            float v = b.infectionChanceMultiplier;
            if (Plugin.Has(Dz.Hiv) && ArvTime <= 0f && Plugin.IsPlayer(b)) v *= Plugin.CfgHivInfection.Value;
            return v;
        }
    }
}