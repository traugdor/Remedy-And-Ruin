using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    /// <summary>
    /// Attached to player entities (see Remedy And RuinModSystem.StartServerSide). Owns all
    /// poison/remedy effect state and application.
    ///
    /// OnItemConsumed: called by Patch_RawEating/Patch_MealEating with a consumed item and its
    /// remedyandruinEffect (or remedyandruinEffectByType) attribute, unparsed.
    /// </summary>
    public class EntityBehaviorRemedyEffects : EntityBehavior
    {
        public ITreeAttribute RREffects
        {
            get => entity.WatchedAttributes.GetTreeAttribute("remedyandruinEffects");
            set
            {
                entity.WatchedAttributes.SetAttribute("remedyandruinEffects", value);
                MarkDirty();
            }
        }
        public void MarkDirty() => entity.WatchedAttributes.MarkPathDirty("remedyandruinEffects");
        public void MarkDirty(string key)
        {
            entity.WatchedAttributes.MarkPathDirty("remedyandruinEffects/" + key);
            parseEffectsAndApply();
        }

        private EffectThreadManager threadManager;

        //============== PPEFFECTS ==============//

        public TreeArrayAttribute RRPoisonEffects
        {
            get => RREffects["rrpoisons"] as TreeArrayAttribute;
            set
            {
                if(value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrpoisons"] = arrvalue;
                    MarkDirty("rrpoisons");
                }
            }
        }
        public TreeArrayAttribute RRIllnessEffects
        {
            get => RREffects["rrillness"] as TreeArrayAttribute;
            set
            {
                if (value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrillness"] = arrvalue;
                    MarkDirty("rrillness");
                }
            }
        }
        public TreeArrayAttribute RRPotionEffects
        {
            get => RREffects["rrpotions"] as TreeArrayAttribute;
            set
            {
                if (value is TreeArrayAttribute arrvalue)
                {
                    RREffects["rrpotions"] = arrvalue;
                    MarkDirty("rrpotions");
                }
            }
        }

        public bool lastKnownEffectsAdvanceOffline
        {
            get => RREffects.GetBool("lastKnownEffectsAdvanceOffline");
            set
            {
                RREffects.SetBool("lastKnownEffectsAdvanceOffline", value);
                MarkDirty();
            }
        }

        //============== TOLERANCES ==============//

        public int toxicTolerance
        {
            get => RREffects.GetInt("toxicTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("toxicTolerance", value);
                MarkDirty();
            }
        }

        public int noxiousTolerance
        {
            get => RREffects.GetInt("noxiousTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("noxiousTolerance", value);
                MarkDirty();
            }
        }

        public int cardiacTolerance
        {
            get => RREffects.GetInt("cardiacTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("cardiacTolerance", value);
                MarkDirty();
            }
        }

        public int neurotoxicTolerance
        {
            get => RREffects.GetInt("neurotoxicTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("neurotoxicTolerance", value);
                MarkDirty();
            }
        }

        public int brainrotTolerance
        {
            get => RREffects.GetInt("brainrotTolerance");
            set
            {
                value = GameMath.Clamp(value, 0, 27);
                RREffects.SetInt("brainrotTolerance", value);
                MarkDirty();
            }
        }

        //============== PROPERTIES ===============//

        bool     drankAntidote = false;
        DateTime timeAntidoteConsumed = DateTime.MinValue;

        //============== CONSTRUCTORS ==============//

        public EntityBehaviorRemedyEffects(Entity entity) : base(entity)
        {
            //if (entity.World.Side != EnumAppSide.Server) return;
            if (!entity.WatchedAttributes.HasAttribute("remedyandruinEffects"))
            {
                entity.WatchedAttributes.SetAttribute("remedyandruinEffects", new TreeAttribute());
            }
            if (RREffects["rrpoisons"] == null)
            {
                RREffects["rrpoisons"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrpoisons");
            }
            if (RREffects["rrillness"] == null)
            {
                RREffects["rrillness"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrillness");
            }
            if (RREffects["rrpotions"] == null)
            {
                RREffects["rrpotions"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
                MarkDirty("rrpotions");
            }
            threadManager = new EffectThreadManager(entity);
            lastKnownEffectsAdvanceOffline = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;
        }

        public void DestroyProgress()
        {
            //I warned you not to.
            brainrotTolerance = 0;
            cardiacTolerance = 0;
            neurotoxicTolerance = 0;
            noxiousTolerance = 0;
            toxicTolerance = 0;
            RREffects["rrpoisons"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrpoisons");
            RREffects["rrillness"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrillness");
            RREffects["rrpotions"] = new TreeArrayAttribute(Array.Empty<TreeAttribute>());
            MarkDirty("rrpotions");
            lastKnownEffectsAdvanceOffline = Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline;
        }

        public override string PropertyName()
        {
            return "remedyandruinEffects";
        }

        //============== Data Structures ==============//

        public enum EffectCluster
        {
            ANALGESIC,
            ANTIDOTE,
            ANTINAUSEA,
            ANTISEPTIC,
            ANTIVIRAL,
            MINDTONIC,
            SEDATIVE,
            TONIC,
            TOXICPOISON,
            NOXIOUSPOISON,
            CARDIACPOISON,
            NEUROTOXICPOISON,
            MINDPOISON,
            TOPICALOINTMENT
        }

        public enum EffectType
        {
            POISON,
            ILLNESS
        }

        public struct EffectStruct (EffectCluster inval)
        {
            public EffectCluster cluster = inval;
            public bool   isConcentrated        = false; //base/potion or concentrate?
            public bool   isPoison              = false; //poison or potion?
            public double timestarted          = 0.0;    //used by all effects with duration
            public double timeleft             = 0.0;    //used by all effects with duration
            public float  effectMultiplier      = 0.0f;  //used by all poisons
            public float  onsetMultiplier       = 0.0f;  //used by some poisons
            public float  toxicEffectMultiplier = 0.0f;  //used by dual-effect mushrooms
            public float  toxicOnsetMultiplier  = 0.0f;  //used by dual-effect mushrooms
        }

        //============== EVENT HANDLERS ==============//

        public void OnItemConsumed(ItemStack consumedStack, JsonObject effectData)
        {
            // Convert effectData.cluster to uppercase for consistency and fill in defaults/parse data
            EffectStruct effect = new EffectStruct(effectData["cluster"].AsString().ToUpper().ToEnum<EffectCluster>());

            if (effectData.KeyExists("tier")) { effect.isConcentrated = effectData["isConcentrated"].AsBool(); }
            if (effectData.KeyExists("isPoison")) { effect.isPoison = effectData["isPoison"].AsBool(); }
            if (effectData.KeyExists("effectMultiplier"))
            {
                effect.effectMultiplier = effectData["effectMultiplier"].AsFloat();
            }
            if (effectData.KeyExists("onsetMultiplier"))
            {
                effect.onsetMultiplier = effectData["onsetMultiplier"].AsFloat();
            }
            if (effectData.KeyExists("toxicEffectMultiplier"))
            {
                effect.toxicEffectMultiplier = effectData["toxicEffectMultiplier"].AsFloat();
            }
            if (effectData.KeyExists("toxicOnsetMultiplier"))
            {
                effect.toxicOnsetMultiplier = effectData["toxicOnsetMultiplier"].AsFloat();
            }

            ApplyEffect(effect);

        }

        private Dictionary<string, bool> effectsApplied = new Dictionary<string, bool>(); //used to prevent double application of the same effect>

        void parseEffectsAndApply()
        {
            //read effects from treeArrayAttribute and apply them. Write to effectsApplied to prevent double application.
            /*
             * rrpoisons
             * rrillness
             * rrpotions
             */

            var liveKeys = new HashSet<string>();
            //use the effect name + UID as actual key.
            //pull rrpoisons
            TreeAttribute[] rrpoisons = RRPoisonEffects.value;
            foreach (var poison in rrpoisons)
            {
                //extract effectname and uid from it.
                string effectname = poison.GetString("effectname");
                string[] t = effectname.Split("|");
                effectname = t[0];
                string guid = t[1];
                liveKeys.Add(guid);
                //lookup effectsApplied using effectuid
                if (!effectsApplied.TryGetValue(guid, out bool applied))
                {
                    //apply effect using name and effect data in rrpoisons
                    //register effect
                    effectsApplied[guid] = true;
                }
                /*
                 * string EffectType
                 * float EffectMult
                 * float EffectOnset
                 * string GUID
                 * DateTime start
                 * object TimeSpanOrStop
                 * string secondaryEffectType = null
                 * float? secondaryEffectMult = null
                 */
                string effecttype = ""; //derive from effectname using some logic switch
                float effectMult = poison.GetFloat("effectMultiplier");
                float effectOnset = poison.GetFloat("onsetMultiplier");
                DateTime start = DateTime.Now;
                object timeSpanOrStop;
                double timeleft = poison.GetDouble("timeleft");
                float toxicEffectMultiplier = poison.GetFloat("toxicEffectMultiplier");
                string secondaryEffectType = toxicEffectMultiplier > 0f ? "toxic" : null;
                float? secondaryEffectMult = toxicEffectMultiplier > 0f ? toxicEffectMultiplier : (float?)null;
                if (Remedy_And_RuinModSystem.Config.allowEffectsToExpireWhenOffline)
                {
                    //hard stop time - calendar hours, not a real DateTime, since this must
                    //track the in-game calendar rather than the wall clock
                    timeSpanOrStop = entity.World.Calendar.TotalHours + timeleft;
                }
                else
                {
                    timeSpanOrStop = timeleft; // we just send the timeleft and calculate the end time on the other side.
                }
                threadManager.ApplyPoisonEffect(effecttype, effectMult, effectOnset, guid, start, timeSpanOrStop, secondaryEffectType, secondaryEffectMult);
            }
            //repeat for potions and illnesses
            TreeAttribute[] rrillnesses = RRIllnessEffects.value;
            foreach (var illness in rrillnesses)
            {
                //extract effectname and uid from it.
                string effectname = illness.GetString("effectname");
                string[] t = effectname.Split("|");
                effectname = t[0];
                string guid = t[1];
                liveKeys.Add(guid);
                //lookup effectsApplied using effectuid
                if (!effectsApplied.TryGetValue(guid, out bool applied))
                {
                    //apply effect using name and effect data in rrillnesses
                    //register effect
                    effectsApplied[guid] = true;
                }
            }

            TreeAttribute[] rrpotions = RRPotionEffects.value;
            foreach (var potion in rrpotions)
            {
                //extract effectname and uid from it.
                string effectname = potion.GetString("effectname");
                string[] t = effectname.Split("|");
                effectname = t[0];
                string guid = t[1];
                liveKeys.Add(guid);
                //lookup effectsApplied using effectuid
                if (!effectsApplied.TryGetValue(guid, out bool applied))
                {
                    //apply effect using name and effect data in rrpotions
                    //register effect
                    effectsApplied[guid] = true;
                }
            }

            foreach (var staleKey in effectsApplied.Keys.Except(liveKeys).ToList())
            {
                effectsApplied.Remove(staleKey);
            }
        }

        //============== ACTUAL CODE ==============//

        public void ApplyEffect(EffectStruct effect) // only called when an item is eaten so it can never apply an illness.
        {
            Guid uid = Guid.NewGuid();
            string effectname = effect.cluster.ToString() + "|" + uid.ToString();
            bool poison = false;
            bool antidote = false;
            switch (effect.cluster)
            {
                case EffectCluster.ANALGESIC:
                    break;
                case EffectCluster.ANTIDOTE:
                    /*
                     * ANTIDOTE:
                     *     - used to indicate antidote
                     *     - drinking once induces vomiting and voids satiety
                     *     - drinking again within one IRL minute removes all poison effects, completely, and entirely.
                     *     - poison effects lost this way do not count towards building tolerance
                     */
                antidote = true;
                    break;
                case EffectCluster.ANTINAUSEA:
                    break;
                case EffectCluster.ANTISEPTIC:
                    break;
                case EffectCluster.ANTIVIRAL:
                    break;
                case EffectCluster.MINDTONIC:
                    break;
                case EffectCluster.SEDATIVE:
                    break;
                case EffectCluster.TONIC:
                    break;
                case EffectCluster.TOXICPOISON:
                    /*
                     * TOXIC POISON :
                     *     - used to indicate liverbane aka liver failure
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(toxicTolerance / 3) / 9.0f
                     *     - apply healingeffectiveness reduction for a certain amount of time
                     *     - once timer expires, add DoT effect if effect > 0.15; DoT never expires
                     *     - if effect < 0.15, do not apply DoT and remove healingeffectiveness reduction
                     *     - surviving this awards 1/27 of progression towards toxicTolerance.
                     */
                    poison = true;
                    break;
                case EffectCluster.NOXIOUSPOISON:
                    /*
                     * NOXIOUS POISON :
                     *     - used to indicate gutbane aka GI irritation
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(noxiousTolerance / 3) / 9.0f
                     *     - apply psychedelic trip effect for a certain amount of time (same as eating a psychedelic mushroom)
                     *     - trigger Vomiting loop for a certain amount of time, lessened by effect multiplier
                     *     - surviving this awards 1/27 of progression towards noxiousTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.CARDIACPOISON:
                    /*
                     * CARDIAC POISON :
                     *     - used to indicate heartbane aka heart failure
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(cardiacTolerance / 3) / 9.0f
                     *     - apply cardiac event (reduce current and max health by 5hp) for a certain amount of time determined by effect multiplier
                     *     - watch player activity and roll the dice on another cardiac event if player uses tools or sprints
                     *     - surviving this awards 1/27 of progression towards cardiacTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.NEUROTOXICPOISON:
                    /*
                     * NEUROTOXIC POISON :
                     *     - used to indicate nervebane aka nerve damage
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(neurotoxicTolerance / 3) / 9.0f
                     *     - apply nerve damage for 6h * effect multiplier
                     *     - additional nerve damage applications will apply the same effect again for max(0, effect multiplier - tolerance value) * (2 ^ n-1 duration) * 6h
                     *     - the 3rd nerve damage application will also apply a cardiac event for 24h * effect multiplier
                     *     - surviving this awards 1/27 of progression towards neurotoxicTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.MINDPOISON:
                    /*
                     * MIND POISON :
                     *     - used to indicate Brain Rot aka mind damage
                     *     - calculate effect by subtracting from effect multiplier the tolerance value calculated by (float)(brainrotTolerance / 3) / 9.0f
                     *     - apply temporal fog effect. strength and duration determined by effect multiplier; baseline full effect for 24h
                     *     - apply psychedelic trip effect. strength and duration determined by effect multiplier; baseline full effect for 24h
                     *     - apply drunken wobble effect. strength and duration determined by effect multiplier; baseline full effect for 24h
                     *     - watch player activity for movement and roll the dice on vomiting if player doesn't hold crouch/block key while moving
                     *     - double hunger rate
                     *     - double thirst rate if HoD is installed.
                     *     - surviving this awards 1/27 of progression towards brainrotTolerance
                     */
                    poison = true;
                    break;
                case EffectCluster.TOPICALOINTMENT:
                    break;
            }
            //write to treeArrayAttribute
            TreeAttribute neweffect = new TreeAttribute();
            neweffect.SetString("effectName", effectname);
            neweffect.SetString("cluster", effect.cluster.ToString());
            neweffect.SetBool("isConcentrated", effect.isConcentrated);
            neweffect.SetDouble("timestarted", effect.timestarted);
            neweffect.SetDouble("timeleft", effect.timeleft);
            neweffect.SetFloat("effectMultiplier", effect.effectMultiplier);
            neweffect.SetFloat("toxicEffectMultiplier", effect.toxicEffectMultiplier);
            neweffect.SetFloat("toxicOnsetMultiplier", effect.toxicOnsetMultiplier);
            if (poison)
            {
                List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
                neweffect.SetBool("isPoison", true);
                neweffect.SetFloat("onsetMultiplier", effect.onsetMultiplier); //only used for poisons
                rrpoisons.Add(neweffect);
                RRPoisonEffects = new TreeArrayAttribute(rrpoisons.ToArray());
            }
            else if (!antidote)
            {
                List<TreeAttribute> rrpotions = RRPotionEffects.value.ToList<TreeAttribute>();
                neweffect.SetBool("isPoison", false);
                rrpotions.Add(neweffect);
                RRPotionEffects = new TreeArrayAttribute(rrpotions.ToArray());
            }
            if (antidote)
            {
                //register time now and set 
                if (!drankAntidote)
                {
                    //first time drinking
                    timeAntidoteConsumed = DateTime.Now;
                    drankAntidote = true;
                    //trigger stomach void
                    VoidStomachContents(new Random().NextDouble());
                }
                else
                {
                    if (timeAntidoteConsumed.AddSeconds(60) > DateTime.Now)
                    {
                        //within the time frame
                        //erase all poison effects
                        List<TreeAttribute> rrpoisons = RRPoisonEffects.value.ToList<TreeAttribute>();
                        rrpoisons.Clear();
                        RRPoisonEffects = new TreeArrayAttribute(rrpoisons.ToArray());
                        drankAntidote = false;
                        timeAntidoteConsumed = DateTime.MinValue;
                    }
                }
            }
        }

        private void VoidStomachContents(double chance)
        {
            float amountToDrain = 0.0f;
            var hunger = entity.GetBehavior<EntityBehaviorHunger>();
            if(hunger != null)
            {
                amountToDrain = hunger.Saturation;
                var stomach = entity.GetBehavior("expandedstomach");
                if (stomach != null)
                {
                    float? currentStomachAmount = stomach.GetType().GetProperty("ExpandedStomachMeter")?.GetValue(stomach) as float?;
                    if (currentStomachAmount.HasValue)
                    {
                        stomach.GetType().GetProperty("ExpandedStomachMeter")?.SetValue(stomach, 0f); //drain stomach first so it doesn't intercept the next call
                    }
                }
                hunger.Saturation -= amountToDrain; //ExpandedStomach will intercept this if it's not empty... 
            }
            //TODO: wire in chance for additional void events if not triggered by antidote
        }
    }

    public static class Helpers
    {
        public static T ToEnum<T>(this string value)
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }
    }
}
