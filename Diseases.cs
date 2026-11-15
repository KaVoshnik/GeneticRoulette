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
        Achromatopsia = 7
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
                NameRu = "ВИЧ (хроническая иммунная недостаточность)",
                NameEn = "HIV (chronic immunodeficiency)",
                DescRu = "Раны инфицируются заметно быстрее.",
                DescEn = "Wounds get infected much faster.",
                // сам эффект — транспилер Limb.Update, см. Patch_LimbUpdate / InfectionMultiplier
                Moodle = ru => new MoodleSpec
                {
                    Bg = 1,
                    Name = ru ? "ВИЧ" : "HIV",
                    Desc = ru ? "Хроническая иммунная недостаточность. Шанс заражения ран ×" + Num(Plugin.CfgHivInfection.Value) + "."
                              : "Chronic immunodeficiency. Wound infection chance x" + Num(Plugin.CfgHivInfection.Value) + ".",
                    Icons = new[] { "lowimmunity", "highimmunity", "infected" }
                }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Hemophilia,
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
                        Desc = ru ? "Время от времени накатывают приступы удушья."
                                  : "Sudden attacks of suffocation come from time to time.",
                        Icons = new[] { "hypoventilation", "cantbreathe", "oxygen" }
                    }
            });

            l.Add(new DiseaseDef
            {
                Id = Dz.Deafness,
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
                NameRu = "Дальтонизм (красно-зелёный)",
                NameEn = "Red-green color blindness",
                DescRu = "Красный и зелёный сливаются.",
                DescEn = "Red and green blur together.",
                Incompatible = new[] { Dz.Achromatopsia },
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
                NameRu = "Ахроматопсия (мир без цвета)",
                NameEn = "Achromatopsia (no color)",
                DescRu = "Весь мир в оттенках серого.",
                DescEn = "The whole world is grayscale.",
                Incompatible = new[] { Dz.Deuteranopia },
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

            return l;
        }

        // =====================================================================
        // Эффекты
        // =====================================================================
        static void AsthmaTick(Body b)
        {
            float dt = Time.deltaTime;
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
            if (Plugin.Has(Dz.Hiv) && Plugin.IsPlayer(b)) v *= Plugin.CfgHivInfection.Value;
            return v;
        }
    }
}