using DndEngine.Domain;
using DndEngine.Domain.Progression;

namespace DndEngine.Infrastructure;

// Adapted mechanical indexes from the official SRD 5.2.1, pp. 27-88 and 91-96.
// Features marked Deferred are represented on the sheet but have no invented automation.
internal static class CharacterRulesSeed
{
    private static readonly string[] Music = ["bagpipes","drum","dulcimer","flute","horn","lute","lyre","pan-flute","shawm","viol"];
    private static readonly string[] Artisan = ["alchemists-supplies","brewers-supplies","calligraphers-supplies","carpenters-tools",
        "cartographers-tools","cobblers-tools","cooks-utensils","glassblowers-tools","jewelers-tools",
        "leatherworkers-tools","masons-tools","painters-supplies","potters-tools","smiths-tools",
        "tinkers-tools","weavers-tools","woodcarvers-tools"];
    private static readonly string[] Games = ["dice-set","dragonchess-set","playing-cards","three-dragon-ante-set"];
    private static readonly string[] Tools = [..Artisan,..Music,..Games,"disguise-kit","forgery-kit","herbalism-kit",
        "navigators-tools","poisoners-kit","thieves-tools"];
    private static FeatureDefinition F(string id, int level, bool deferred = true, params FeatureEffect[] effects) =>
        new(id, id.Replace('-', ' '), level, effects, Deferred: deferred);
    private static FeatureEffect E(EffectKind kind, string target = "", int amount = 0, int[]? values = null, RecoveryKind recovery = RecoveryKind.LongRest, Ability? scaling = null) => new(kind, target, amount, values, recovery, scaling);
    private static ClassDefinition C(string id, int die, Ability[] primary, Ability[] saves, string skills, int count,
        string weapons, string armor, string mcWeapons, string mcArmor, string equipment, params FeatureDefinition[] features) =>
        new(id, id, die, primary, saves, skills.Split('|'), count, weapons.Split('|', StringSplitOptions.RemoveEmptyEntries),
            armor.Split('|', StringSplitOptions.RemoveEmptyEntries), mcWeapons.Split('|', StringSplitOptions.RemoveEmptyEntries),
            mcArmor.Split('|', StringSplitOptions.RemoveEmptyEntries), equipment.Split('|', StringSplitOptions.RemoveEmptyEntries),
            [..features, ..new[] {4,8,12,16}.Concat(id == "fighter" ? [6,14] : id == "rogue" ? [10] : []).Select(n => F("ability-score-improvement",n)), F("epic-boon",19)],
            id == "fighter", id is "bard" or "ranger" or "rogue" ? 1 : 0,
            id == "rogue" ? ["thieves-tools"] : [],
            [new SubclassDefinition(id switch {
                "barbarian" => "path-of-the-berserker", "bard" => "college-of-lore", "cleric" => "life-domain",
                "druid" => "circle-of-the-land", "fighter" => "champion", "monk" => "warrior-of-the-open-hand",
                "paladin" => "oath-of-devotion", "ranger" => "hunter", "rogue" => "thief",
                "sorcerer" => "draconic-sorcery", "warlock" => "fiend-patron", "wizard" => "school-of-evocation",
                _ => throw new InvalidOperationException("Unknown SRD class") }, "SRD subclass", [F("subclass-features",3)])],
            id switch { "druid" => ["herbalism-kit"], "rogue" => ["thieves-tools"], _ => [] },
            id == "bard" ? 3 : id == "monk" ? 1 : 0,
            id == "bard" ? Music : id == "monk" ? [..Music,..Artisan] : [], id == "bard" ? 1 : 0);
    private static SpeciesDefinition S(string id, string sizes, int speed, FeatureDefinition[] features, params VariantDefinition[] variants) =>
        new(id, id, sizes.Split('|'), speed, features, variants,
            id == "elf" ? ["insight","perception","survival"] : id == "human" ?
            ["acrobatics","animal-handling","arcana","athletics","deception","history","insight","intimidation",
             "investigation","medicine","nature","perception","performance","persuasion","religion","sleight-of-hand",
             "stealth","survival"] : [], id is "elf" or "human" ? 1 : 0);
    private static VariantDefinition V(string id, params FeatureDefinition[] features) => new(id, id, features);
    private static BackgroundDefinition B(string id, Ability a, Ability b, Ability c, string skills, string tool, string feat, string gear) =>
        new(id, id, [a,b,c], skills.Split('|'), tool, feat, gear.Split('|'), [],
            id == "soldier" ? Games : []);
    private static FeatDefinition Feat(string id, FeatKind kind, int level = 1, bool repeat = false, bool deferred = true, params FeatureEffect[] effects) =>
        new(id, id, kind, level, null, 0, repeat, effects, deferred);
    private static FeatDefinition Epic(string id, params Ability[] options) => new(id,id,FeatKind.EpicBoon,19,null,0,false,[],true,
        AbilityBoostOptions:options.Length==0 ? Enum.GetValues<Ability>() : options,AbilityBoostAmount:1,AbilityBoostCap:30);
    private static ItemDefinition A(string id, ArmorKind kind, int ac, int dex = 99, int strength = 0, bool stealth = false) =>
        new(id, id, kind == ArmorKind.Shield ? ItemKind.Shield : ItemKind.Armor, kind, ac, dex, strength, stealth);
    private static ItemDefinition G(string id) => new(id, id, ItemKind.Gear);

    public static CharacterRules Create() => new(
        [
            S("dragonborn", "Medium", 30, [F("draconic-ancestry",1), F("breath-weapon",1), F("damage-resistance",1), F("darkvision",1,false,E(EffectKind.Darkvision,amount:60)), F("draconic-flight",5)],
                V("black",F("acid-resistance",1,false,E(EffectKind.Resistance,"Acid"))), V("blue",F("lightning-resistance",1,false,E(EffectKind.Resistance,"Lightning"))),
                V("brass",F("fire-resistance",1,false,E(EffectKind.Resistance,"Fire"))), V("bronze",F("lightning-resistance",1,false,E(EffectKind.Resistance,"Lightning"))),
                V("copper",F("acid-resistance",1,false,E(EffectKind.Resistance,"Acid"))), V("gold",F("fire-resistance",1,false,E(EffectKind.Resistance,"Fire"))),
                V("green",F("poison-resistance",1,false,E(EffectKind.Resistance,"Poison"))), V("red",F("fire-resistance",1,false,E(EffectKind.Resistance,"Fire"))),
                V("silver",F("cold-resistance",1,false,E(EffectKind.Resistance,"Cold"))), V("white",F("cold-resistance",1,false,E(EffectKind.Resistance,"Cold")))),
            S("dwarf", "Medium", 30, [F("darkvision-120",1,false,E(EffectKind.Darkvision,amount:120)), F("dwarven-resilience",1,true,E(EffectKind.Resistance,"Poison")), F("dwarven-toughness",1,false,E(EffectKind.HitPointsPerLevel,amount:1)), F("stonecunning",1)]),
            S("elf", "Medium", 30, [F("darkvision",1,false,E(EffectKind.Darkvision,amount:60)), F("fey-ancestry",1), F("keen-senses",1), F("trance",1)],
                V("drow",F("drow-lineage",1)), V("high",F("high-elf-lineage",1)), V("wood",F("wood-elf-lineage",1,false,E(EffectKind.SpeedBonus,amount:5)))),
            S("gnome", "Small", 30, [F("darkvision",1,false,E(EffectKind.Darkvision,amount:60)), F("gnomish-cunning",1)], V("forest",F("forest-gnome-lineage",1)), V("rock",F("rock-gnome-lineage",1))),
            S("goliath", "Medium", 35, [F("giant-ancestry",1), F("large-form",5), F("powerful-build",1)],
                V("cloud"),V("fire"),V("frost"),V("hill"),V("stone"),V("storm")),
            S("halfling", "Small", 30, [F("brave",1),F("halfling-nimbleness",1),F("luck",1),F("naturally-stealthy",1)]),
            S("human", "Small|Medium", 30, [F("resourceful",1),F("skillful",1),F("versatile",1)]),
            S("orc", "Medium", 30, [F("adrenaline-rush",1),F("darkvision-120",1,false,E(EffectKind.Darkvision,amount:120)),F("relentless-endurance",1)]),
            S("tiefling", "Small|Medium", 30, [F("darkvision",1,false,E(EffectKind.Darkvision,amount:60)),F("otherworldly-presence",1)],
                V("abyssal",F("abyssal-resistance",1,false,E(EffectKind.Resistance,"Poison"))),
                V("chthonic",F("chthonic-resistance",1,false,E(EffectKind.Resistance,"Necrotic"))),
                V("infernal",F("infernal-resistance",1,false,E(EffectKind.Resistance,"Fire"))))
        ],
        [
            B("acolyte",Ability.Intelligence,Ability.Wisdom,Ability.Charisma,"insight|religion","calligraphers-supplies","magic-initiate-cleric","calligraphers-supplies|holy-symbol|priest-pack"),
            B("criminal",Ability.Dexterity,Ability.Constitution,Ability.Intelligence,"sleight-of-hand|stealth","thieves-tools","alert","thieves-tools|crowbar|burglar-pack"),
            B("sage",Ability.Constitution,Ability.Intelligence,Ability.Wisdom,"arcana|history","calligraphers-supplies","magic-initiate-wizard","calligraphers-supplies|quarterstaff|scholar-pack"),
            B("soldier",Ability.Strength,Ability.Dexterity,Ability.Constitution,"athletics|intimidation","gaming-set-choice","savage-attacker","spear|shortbow|quiver|explorer-pack")
        ],
        [
            C("barbarian",12,[Ability.Strength],[Ability.Strength,Ability.Constitution],"animal-handling|athletics|intimidation|nature|perception|survival",2,"simple|martial","light|medium|shield","martial","shield","greataxe|handaxe|explorer-pack",
                F("rage",1,true,E(EffectKind.Resource,"rage",0,[2,2,3,3,3,4,4,4,4,4,4,5,5,5,5,5,6,6,6,6],RecoveryKind.OneOnShortRest)),F("unarmored-defense",1,false,E(EffectKind.UnarmoredDefense,"Constitution",1)),F("weapon-mastery",1,true,E(EffectKind.WeaponMasterySlots,"melee",2,[2,2,2,3,3,3,3,3,3,4,4,4,4,4,4,4,4,4,4,4])),F("reckless-attack",2),F("danger-sense",2),F("barbarian-subclass",3),F("primal-knowledge",3),F("extra-attack",5,false,E(EffectKind.ExtraAttack,amount:2)),F("fast-movement",5,false,E(EffectKind.SpeedBonus,"no-heavy",10))),
            C("bard",8,[Ability.Charisma],[Ability.Dexterity,Ability.Charisma],"*",3,"simple","light","","light","leather-armor|dagger|musical-instrument|entertainer-pack",
                F("bardic-inspiration",1,true,E(EffectKind.Resource,"bardic-inspiration",1,scaling:Ability.Charisma)),F("spellcasting",1),F("expertise",2),F("jack-of-all-trades",2),F("bard-subclass",3),F("font-of-inspiration",5,true,E(EffectKind.ResourceRecovery,"bardic-inspiration",recovery:RecoveryKind.ShortRest)),F("countercharm",7),F("magical-secrets",10),F("superior-inspiration",18),F("words-of-creation",20)),
            C("cleric",8,[Ability.Wisdom],[Ability.Wisdom,Ability.Charisma],"history|insight|medicine|persuasion|religion",2,"simple","light|medium|shield","","light|medium|shield","chain-shirt|shield|mace|holy-symbol|priest-pack",
                F("spellcasting",1),F("divine-order",1),F("channel-divinity",2,true,E(EffectKind.Resource,"channel-divinity",0,[0,2,2,2,2,3,3,3,3,3,3,3,3,3,3,3,3,3,4,4,4],RecoveryKind.OneOnShortRest)),F("cleric-subclass",3),F("sear-undead",5),F("blessed-strikes",7),F("divine-intervention",10),F("improved-blessed-strikes",14),F("greater-divine-intervention",20)),
            C("druid",8,[Ability.Wisdom],[Ability.Intelligence,Ability.Wisdom],"animal-handling|arcana|insight|medicine|nature|perception|religion|survival",2,"simple","light|shield","","light|shield","leather-armor|shield|sickle|quarterstaff|explorer-pack|herbalism-kit",
                F("spellcasting",1),F("druidic",1),F("primal-order",1),F("wild-shape",2,true,E(EffectKind.Resource,"wild-shape",0,[0,2,2,2,2,2,2,2,2,3,3,3,3,3,3,3,3,4,4,4],RecoveryKind.OneOnShortRest)),F("wild-companion",2),F("druid-subclass",3),F("wild-resurgence",5),F("elemental-fury",7),F("improved-elemental-fury",15),F("beast-spells",18),F("archdruid",20)),
            C("fighter",10,[Ability.Strength,Ability.Dexterity],[Ability.Strength,Ability.Constitution],"acrobatics|animal-handling|athletics|history|insight|intimidation|perception|persuasion|survival",2,"simple|martial","light|medium|heavy|shield","martial","light|medium|shield","chain-mail|greatsword|flail|javelin|dungeoneer-pack",
                F("fighting-style",1),F("second-wind",1,true,E(EffectKind.Resource,"second-wind",0,[2,2,2,3,3,3,3,3,3,4,4,4,4,4,4,4,4,4,4,4],RecoveryKind.OneOnShortRest)),F("weapon-mastery",1,true,E(EffectKind.WeaponMasterySlots,amount:3,values:[3,3,3,4,4,4,4,4,4,5,5,5,5,5,5,6,6,6,6,6])),F("action-surge",2,true,E(EffectKind.Resource,"action-surge",0,[0,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,2,2,2,2],RecoveryKind.ShortRest)),F("tactical-mind",2),F("fighter-subclass",3),F("extra-attack",5,false,E(EffectKind.ExtraAttack,amount:2)),F("tactical-shift",5),F("indomitable",9),F("tactical-master",9),F("two-extra-attacks",11,false,E(EffectKind.ExtraAttack,amount:3)),F("studied-attacks",13),F("three-extra-attacks",20,false,E(EffectKind.ExtraAttack,amount:4))),
            C("monk",8,[Ability.Dexterity,Ability.Wisdom],[Ability.Strength,Ability.Dexterity],"acrobatics|athletics|history|insight|religion|stealth",2,"simple|martial-light","","","","spear|dagger|explorer-pack",
                F("martial-arts",1),F("unarmored-defense",1,false,E(EffectKind.UnarmoredDefense,"Wisdom")),F("monks-focus",2,true,E(EffectKind.Resource,"focus",0,[0,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20],RecoveryKind.ShortRest)),F("unarmored-movement",2,false,E(EffectKind.SpeedBonus,"unarmored",0,[0,10,10,10,10,15,15,15,15,20,20,20,20,25,25,25,25,30,30,30])),F("uncanny-metabolism",2),F("deflect-attacks",3),F("monk-subclass",3),F("slow-fall",4),F("extra-attack",5,false,E(EffectKind.ExtraAttack,amount:2)),F("stunning-strike",5),F("empowered-strikes",6),F("evasion",7),F("acrobatic-movement",9),F("heightened-focus",10),F("self-restoration",10),F("deflect-energy",13),F("disciplined-survivor",14),F("perfect-focus",15),F("superior-defense",18),F("body-and-mind",20)),
            C("paladin",10,[Ability.Strength,Ability.Charisma],[Ability.Wisdom,Ability.Charisma],"athletics|insight|intimidation|medicine|persuasion|religion",2,"simple|martial","light|medium|heavy|shield","martial","light|medium|shield","chain-mail|shield|longsword|javelin|holy-symbol|priest-pack",
                F("lay-on-hands",1,true,E(EffectKind.Resource,"lay-on-hands",0,Enumerable.Range(1,20).Select(x=>x*5).ToArray())),F("weapon-mastery",1,true,E(EffectKind.WeaponMasterySlots,amount:2)),F("spellcasting",1),F("fighting-style",2),F("paladins-smite",2),F("channel-divinity",3,true,E(EffectKind.Resource,"paladin-channel-divinity",0,[0,0,2,2,2,2,2,2,2,2,3,3,3,3,3,3,3,3,3,3],RecoveryKind.OneOnShortRest)),F("paladin-subclass",3),F("extra-attack",5,false,E(EffectKind.ExtraAttack,amount:2)),F("faithful-steed",5),F("aura-of-protection",6),F("abjure-foes",9),F("aura-of-courage",10),F("radiant-strikes",11),F("restoring-touch",14),F("aura-expansion",18)),
            C("ranger",10,[Ability.Dexterity,Ability.Wisdom],[Ability.Strength,Ability.Dexterity],"animal-handling|athletics|insight|investigation|nature|perception|stealth|survival",3,"simple|martial","light|medium|shield","martial","light|medium|shield","studded-leather-armor|scimitar|shortsword|longbow|quiver|explorer-pack",
                F("favored-enemy",1),F("spellcasting",1),F("weapon-mastery",1,true,E(EffectKind.WeaponMasterySlots,amount:2)),F("deft-explorer",2),F("fighting-style",2),F("ranger-subclass",3),F("extra-attack",5,false,E(EffectKind.ExtraAttack,amount:2)),F("roving",6,true,E(EffectKind.SpeedBonus,"no-heavy",10)),F("expertise",9),F("tireless",10),F("relentless-hunter",13),F("natures-veil",14),F("precise-hunter",17),F("feral-senses",18),F("foe-slayer",20)),
            C("rogue",8,[Ability.Dexterity],[Ability.Dexterity,Ability.Intelligence],"acrobatics|athletics|deception|insight|intimidation|investigation|perception|persuasion|sleight-of-hand|stealth",4,"simple|martial-finesse-or-light","light","","light","leather-armor|dagger|shortsword|shortbow|quiver|thieves-tools|burglar-pack",
                F("expertise",1),F("sneak-attack",1),F("thieves-cant",1),F("weapon-mastery",1,true,E(EffectKind.WeaponMasterySlots,amount:2)),F("cunning-action",2),F("rogue-subclass",3),F("steady-aim",3),F("cunning-strike",5),F("uncanny-dodge",5),F("expertise",6),F("evasion",7),F("reliable-talent",7),F("improved-cunning-strike",11),F("devious-strikes",14),F("slippery-mind",15,false,E(EffectKind.Proficiency,"Save:Wisdom"),E(EffectKind.Proficiency,"Save:Charisma")),F("elusive",18),F("stroke-of-luck",20)),
            C("sorcerer",6,[Ability.Charisma],[Ability.Constitution,Ability.Charisma],"arcana|deception|insight|intimidation|persuasion|religion",2,"simple","","","","spear|dagger|arcane-focus|dungeoneer-pack",
                F("spellcasting",1),F("innate-sorcery",1,true,E(EffectKind.Resource,"innate-sorcery",0,Enumerable.Repeat(2,20).ToArray())),F("font-of-magic",2),F("metamagic",2),F("sorcerer-subclass",3),F("sorcerous-restoration",5),F("sorcery-incarnate",7),F("metamagic",10),F("metamagic",17),F("arcane-apotheosis",20)),
            C("warlock",8,[Ability.Charisma],[Ability.Wisdom,Ability.Charisma],"arcana|deception|history|intimidation|investigation|nature|religion",2,"simple","light","","light","leather-armor|sickle|dagger|arcane-focus|scholar-pack",
                F("eldritch-invocations",1),F("pact-magic",1),F("magical-cunning",2),F("warlock-subclass",3),F("contact-patron",9),F("mystic-arcanum-6",11),F("mystic-arcanum-7",13),F("mystic-arcanum-8",15),F("mystic-arcanum-9",17),F("eldritch-master",20)),
            C("wizard",6,[Ability.Intelligence],[Ability.Intelligence,Ability.Wisdom],"arcana|history|insight|investigation|medicine|nature|religion",2,"simple","","","","dagger|quarterstaff|arcane-focus|spellbook|scholar-pack",
                F("spellcasting",1),F("ritual-adept",1),F("arcane-recovery",1),F("scholar",2),F("wizard-subclass",3),F("memorize-spell",5),F("spell-mastery",18),F("signature-spells",20))
        ],
        [
            Feat("alert",FeatKind.Origin, 1, false, true, E(EffectKind.InitiativeBonus,"proficiency")),
            Feat("magic-initiate-cleric",FeatKind.Origin),Feat("magic-initiate-druid",FeatKind.Origin),Feat("magic-initiate-wizard",FeatKind.Origin),
            Feat("savage-attacker",FeatKind.Origin),new FeatDefinition("skilled","Skilled",FeatKind.Origin,1,null,0,true,[],true,ProficiencyChoiceCount:3),
            Feat("ability-score-improvement",FeatKind.General,4,repeat:true),
            new FeatDefinition("grappler","Grappler",FeatKind.General,4,null,13,false,[],true,
                AnyAbilityPrerequisites:[Ability.Strength,Ability.Dexterity],AbilityBoostOptions:[Ability.Strength,Ability.Dexterity],AbilityBoostAmount:1),
            Feat("archery",FeatKind.FightingStyle),Feat("defense",FeatKind.FightingStyle, 1, false, false, E(EffectKind.ArmorClassBonus,"armored",1)),
            Feat("great-weapon-fighting",FeatKind.FightingStyle),Feat("two-weapon-fighting",FeatKind.FightingStyle),
            Epic("boon-of-combat-prowess"),Epic("boon-of-dimensional-travel"),
            Epic("boon-of-fate"),Epic("boon-of-irresistible-offense",Ability.Strength,Ability.Dexterity),
            Epic("boon-of-spell-recall",Ability.Intelligence,Ability.Wisdom,Ability.Charisma),
            Epic("boon-of-the-night-spirit"),Epic("boon-of-truesight")
        ],
        [
            A("padded-armor",ArmorKind.Light,11,stealth:true),A("leather-armor",ArmorKind.Light,11),A("studded-leather-armor",ArmorKind.Light,12),
            A("hide-armor",ArmorKind.Medium,12,2),A("chain-shirt",ArmorKind.Medium,13,2),A("scale-mail",ArmorKind.Medium,14,2,stealth:true),
            A("breastplate",ArmorKind.Medium,14,2),A("half-plate-armor",ArmorKind.Medium,15,2,stealth:true),
            A("ring-mail",ArmorKind.Heavy,14,0,stealth:true),A("chain-mail",ArmorKind.Heavy,16,0,13,true),
            A("splint-armor",ArmorKind.Heavy,17,0,15,true),A("plate-armor",ArmorKind.Heavy,18,0,15,true),
            A("shield",ArmorKind.Shield,2),
            G("calligraphers-supplies"),G("thieves-tools"),G("gaming-set-choice"),G("herbalism-kit"),G("holy-symbol"),
            G("priest-pack"),G("burglar-pack"),G("scholar-pack"),G("explorer-pack"),G("dungeoneer-pack"),G("entertainer-pack"),
            G("arcane-focus"),G("musical-instrument"),G("quiver"),G("spellbook"),G("crowbar")
        ], Tools);
}
