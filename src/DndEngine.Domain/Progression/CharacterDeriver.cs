using DndEngine.Domain.Combat;

namespace DndEngine.Domain.Progression;

public static class CharacterDeriver
{
    private static readonly (string Id, Ability Ability)[] Skills = [
        ("acrobatics",Ability.Dexterity),("animal-handling",Ability.Wisdom),("arcana",Ability.Intelligence),
        ("athletics",Ability.Strength),("deception",Ability.Charisma),("history",Ability.Intelligence),
        ("insight",Ability.Wisdom),("intimidation",Ability.Charisma),("investigation",Ability.Intelligence),
        ("medicine",Ability.Wisdom),("nature",Ability.Intelligence),("perception",Ability.Wisdom),
        ("performance",Ability.Charisma),("persuasion",Ability.Charisma),("religion",Ability.Intelligence),
        ("sleight-of-hand",Ability.Dexterity),("stealth",Ability.Dexterity),("survival",Ability.Wisdom) ];
    public static bool IsSkillId(string id) => Skills.Any(x => x.Id == id);

    public static CharacterSheet Derive(Character character, ProgressionState state, CharacterRules rules, CombatContent combat)
    {
        var species = rules.Species.SingleOrDefault(x => x.Id == state.SpeciesId) ?? throw new RuleViolation("Unknown species.");
        var background = rules.Backgrounds.SingleOrDefault(x => x.Id == state.BackgroundId) ?? throw new RuleViolation("Unknown background.");
        if (!species.Sizes.Contains(state.Size)) throw new RuleViolation("Invalid species size.");
        var variant = species.Variants.SingleOrDefault(x => x.Id == state.SpeciesVariantId);
        if (species.Variants.Length > 0 && variant is null || species.Variants.Length == 0 && state.SpeciesVariantId is not null)
            throw new RuleViolation("A valid species variant is required.");
        if (state.Classes.Length == 0 || state.Classes.Any(x => x.Level is < 1 or > 20) ||
            state.Classes.Select(x => x.ClassId).Distinct().Count() != state.Classes.Length ||
            state.Classes.Sum(x => x.Level) != character.Level.Value)
            throw new RuleViolation("Class levels must be unique and sum to character level.");
        if (state.HitDice.Sum(x => x.Total) != character.Level.Value || state.HitDice.Any(x => x.Available < 0 || x.Available > x.Total))
            throw new RuleViolation("Invalid hit-die pools.");
        var abilities = Enum.GetValues<Ability>().ToDictionary(a => a, a =>
        {
            if (!state.BaseAbilities.TryGetValue(a, out var value)) throw new RuleViolation("All base abilities are required.");
            Guard.Range(value, 1, 20, "Base ability");
            return Guard.Range(value + state.BackgroundBonuses.GetValueOrDefault(a) + state.AdvancementBonuses.GetValueOrDefault(a), 1, 30, "Derived ability");
        });
        if (state.BackgroundBonuses.Keys.Any(a => !background.AbilityChoices.Contains(a)) ||
            state.BackgroundBonuses.Values.Any(v => v is < 1 or > 2) ||
            state.BackgroundBonuses.Values.Order().SequenceEqual([1,2]) == false &&
            state.BackgroundBonuses.Values.Order().SequenceEqual([1,1,1]) == false)
            throw new RuleViolation("Background bonuses must be +2/+1 or +1/+1/+1 among its three abilities.");
        var modifiers = abilities.ToDictionary(x => x.Key, x => new AbilityScore(x.Value).Modifier);
        var level = character.Level.Value;
        var proficiencyBonus = character.Level.ProficiencyBonus;
        var classes = state.Classes.Select(x => (Level:x, Definition:rules.Classes.SingleOrDefault(c => c.Id == x.ClassId)
            ?? throw new RuleViolation("Unknown class."))).ToArray();
        var features = new List<FeatureGrant>();
        var effects = new List<(FeatureEffect Effect, string Source, int ClassLevel)>();
        void Grant(FeatureDefinition feature, string source, int sourceLevel)
        {
            if (feature.Level > sourceLevel) return;
            features.Add(new(feature.Id, feature.Name, source, feature.Deferred));
            effects.AddRange(feature.Effects.Select(e => (e,source,sourceLevel)));
        }
        foreach (var f in species.Features) Grant(f, $"species:{species.Id}", level);
        if (variant is not null) foreach (var f in variant.Features) Grant(f, $"species:{species.Id}/{variant.Id}", level);
        foreach (var f in background.Features) Grant(f, $"background:{background.Id}", level);
        foreach (var (allocation, definition) in classes)
            foreach (var f in definition.Features) Grant(f, $"class:{definition.Id}", allocation.Level);
        foreach (var (allocation, definition) in classes.Where(x => x.Level.Level >= 3))
        {
            if (state.SubclassIds is null || !state.SubclassIds.TryGetValue(definition.Id,out var subclassId))
                throw new RuleViolation("A subclass choice is required at class level 3.");
            var subclass = (definition.Subclasses ?? []).SingleOrDefault(x => x.Id == subclassId)
                ?? throw new RuleViolation("Unknown subclass choice.");
            foreach (var f in subclass.Features) Grant(f,$"subclass:{subclass.Id}",allocation.Level);
        }
        foreach (var featId in state.FeatIds)
        {
            var feat = rules.Feats.SingleOrDefault(x => x.Id == featId) ?? throw new RuleViolation("Unknown feat.");
            features.Add(new(feat.Id, feat.Name, $"feat:{feat.Id}", feat.Deferred));
            effects.AddRange(feat.Effects.Select(e => (e,$"feat:{feat.Id}",level)));
        }
        if (!state.FeatIds.Contains(background.OriginFeat)) throw new RuleViolation("Background origin feat is missing.");
        var first = classes[0].Definition;
        if (state.ClassSkills.Length != first.SkillChoiceCount || state.ClassSkills.Distinct().Count() != state.ClassSkills.Length ||
            state.ClassSkills.Any(x => !(first.SkillChoices.Contains("*") || first.SkillChoices.Contains(x)) ||
                background.Skills.Contains(x) || !Skills.Any(s => s.Id == x)))
            throw new RuleViolation("Invalid starting class skill choices.");
        var proficiencies = new List<Proficiency>();
        void Add(ProficiencyKind kind, string id, string source)
        {
            if (!proficiencies.Any(x => x.Kind == kind && x.Id == id)) proficiencies.Add(new(kind,id,source));
        }
        foreach (var skill in background.Skills) Add(ProficiencyKind.Skill,skill,$"background:{background.Id}");
        if (background.ToolChoices is { Length: > 0 } choices)
        {
            if (state.BackgroundToolId is null || !choices.Contains(state.BackgroundToolId))
                throw new RuleViolation("A valid background tool choice is required.");
            Add(ProficiencyKind.Tool,state.BackgroundToolId,$"background:{background.Id}");
        }
        else
        {
            if (state.BackgroundToolId is not null) throw new RuleViolation("This background has no tool choice.");
            Add(ProficiencyKind.Tool,background.Tool,$"background:{background.Id}");
        }
        foreach (var skill in state.ClassSkills) Add(ProficiencyKind.Skill,skill,$"class:{first.Id}");
        if ((state.ClassTools?.Length ?? 0) != first.ToolChoiceCount ||
            (state.ClassTools ?? []).Distinct().Count() != (state.ClassTools?.Length ?? 0) ||
            (state.ClassTools ?? []).Any(x => !(first.ToolChoiceOptions ?? []).Contains(x)))
            throw new RuleViolation("Invalid class tool choices.");
        foreach (var tool in first.ToolTraining ?? []) Add(ProficiencyKind.Tool,tool,$"class:{first.Id}");
        foreach (var tool in state.ClassTools ?? []) Add(ProficiencyKind.Tool,tool,$"class:{first.Id}");
        if (species.SkillChoiceCount > 0)
        {
            if (state.SpeciesSkill is null || !(species.SkillChoices ?? []).Contains(state.SpeciesSkill))
                throw new RuleViolation("A valid species skill choice is required.");
            Add(ProficiencyKind.Skill,state.SpeciesSkill,$"species:{species.Id}");
        }
        else if (state.SpeciesSkill is not null) throw new RuleViolation("This species has no skill choice.");
        foreach (var save in first.Saves) Add(ProficiencyKind.Save,save.ToString(),$"class:{first.Id}");
        foreach (var (allocation, definition) in classes)
        {
            var initial = definition.Id == first.Id;
            foreach (var weapon in initial ? definition.WeaponTraining : definition.MulticlassWeaponTraining)
                Add(ProficiencyKind.Weapon,weapon,$"class:{definition.Id}");
            foreach (var armorTraining in initial ? definition.ArmorTraining : definition.MulticlassArmorTraining)
                Add(ProficiencyKind.Armor,armorTraining,$"class:{definition.Id}");
        }
        foreach (var (effect,source,_) in effects.Where(x => x.Effect.Kind == EffectKind.Proficiency))
        {
            var pair = effect.Target.Split(':',2);
            if (pair.Length != 2 || !Enum.TryParse<ProficiencyKind>(pair[0],true,out var kind)) throw new RuleViolation("Invalid proficiency effect.");
            Add(kind,pair[1],source);
        }
        foreach (var extra in state.ExtraProficiencies ?? []) Add(extra.Kind,extra.Id,extra.Source);
        var saves = Enum.GetValues<Ability>().Select(a => new DerivedSave(a, Statistic(modifiers[a],a.ToString(),
            proficiencies.Any(p => p.Kind == ProficiencyKind.Save && p.Id == a.ToString()) ? proficiencyBonus : 0))).ToArray();
        var skills = Skills.Select(s => new DerivedSkill(s.Id,s.Ability,Statistic(modifiers[s.Ability],s.Ability.ToString(),
            proficiencies.Any(p => p.Kind == ProficiencyKind.Skill && p.Id == s.Id) ? proficiencyBonus : 0))).ToArray();
        var inventory = state.Inventory.Select(x => (Instance:x, Definition:FindItem(x.DefinitionId,rules,combat))).ToArray();
        if (inventory.Select(x => x.Instance.Id).Distinct().Count() != inventory.Length) throw new RuleViolation("Duplicate item IDs.");
        var armor = inventory.Where(x => x.Instance.Equipped && x.Definition.Kind == ItemKind.Armor).ToArray();
        var shields = inventory.Where(x => x.Instance.Equipped && x.Definition.Kind == ItemKind.Shield).ToArray();
        if (armor.Length > 1 || shields.Length > 1) throw new RuleViolation("Only one armor and one shield can be equipped.");
        var untrainedArmor = armor.Any(x => !proficiencies.Any(p => p.Kind == ProficiencyKind.Armor &&
            p.Id == x.Definition.ArmorKind!.Value.ToString().ToLowerInvariant()));
        var acParts = new List<StatisticPart> { new(armor.Length == 0 ? "Unarmored base" : armor[0].Definition.Name,
            armor.Length == 0 ? 10 : armor[0].Definition.BaseAc) };
        var dexAc = armor.Length == 0 ? modifiers[Ability.Dexterity] :
            armor[0].Definition.ArmorKind == ArmorKind.Heavy ? 0 :
            Math.Min(modifiers[Ability.Dexterity],armor[0].Definition.MaxDexterityBonus);
        if (dexAc != 0) acParts.Add(new("Dexterity",dexAc));
        var baseAc = acParts.Sum(x => x.Value);
        if (armor.Length == 0)
        {
            foreach (var (effect,source,_) in effects.Where(x => x.Effect.Kind == EffectKind.UnarmoredDefense))
            {
                if (shields.Length > 0 && effect.Amount != 1) continue;
                if (!Enum.TryParse<Ability>(effect.Target,out var ability)) throw new RuleViolation("Unknown unarmored ability.");
                var candidate = 10 + modifiers[Ability.Dexterity] + modifiers[ability];
                if (candidate > baseAc)
                {
                    acParts.Clear(); acParts.Add(new("Unarmored base",10));
                    acParts.Add(new("Dexterity",modifiers[Ability.Dexterity]));
                    acParts.Add(new(source + " " + ability,modifiers[ability]));
                    baseAc = candidate;
                }
            }
        }
        if (shields.Length > 0 && proficiencies.Any(p => p.Kind == ProficiencyKind.Armor && p.Id == "shield"))
            acParts.Add(new("Shield",2));
        foreach (var (effect,source,_) in effects.Where(x => x.Effect.Kind == EffectKind.ArmorClassBonus &&
            (x.Effect.Target != "armored" || armor.Length > 0))) acParts.Add(new(source,effect.Amount));
        var armorClass = new DerivedStatistic(acParts.Sum(x => x.Value),acParts.ToArray());
        var speedParts = new List<StatisticPart> { new($"species:{species.Id}",species.Speed) };
        speedParts.AddRange(effects.Where(x => x.Effect.Kind == EffectKind.SpeedBonus &&
            (x.Effect.Target != "unarmored" || armor.Length == 0 && shields.Length == 0) &&
            (x.Effect.Target != "no-heavy" || armor.Length == 0 || armor[0].Definition.ArmorKind != ArmorKind.Heavy))
            .Select(x => new StatisticPart(x.Source,x.Effect.Values is { } values ? values[x.ClassLevel-1] : x.Effect.Amount)));
        if (armor.Length > 0 && armor[0].Definition.StrengthRequired > abilities[Ability.Strength])
            speedParts.Add(new("Armor Strength requirement",-10));
        var speed = speedParts.Sum(x => x.Value);
        var resistance = effects.Where(x => x.Effect.Kind == EffectKind.Resistance)
            .Select(x => Enum.Parse<DamageType>(x.Effect.Target)).Distinct().ToArray();
        var darkvision = effects.Where(x => x.Effect.Kind == EffectKind.Darkvision)
            .Select(x => x.Effect.Amount).DefaultIfEmpty(0).Max();
        var attackCount = Math.Max(1,effects.Where(x => x.Effect.Kind == EffectKind.ExtraAttack).Select(x => x.Effect.Amount).DefaultIfEmpty(1).Max());
        var masteryEffects = effects.Where(x => x.Effect.Kind == EffectKind.WeaponMasterySlots).ToArray();
        var masterySlots = masteryEffects.Sum(x => x.Effect.Values is { } values ? values[x.ClassLevel-1] : x.Effect.Amount);
        if (state.MasteredWeaponIds.Length > masterySlots || state.MasteredWeaponIds.Distinct().Count() != state.MasteredWeaponIds.Length ||
            state.MasteredWeaponIds.Any(id => !combat.Weapons.Any(w => w.Id == id) || !WeaponProficient(id,proficiencies,combat) ||
                !masteryEffects.Any(e => e.Effect.Target != "melee" || combat.Weapons.Single(w => w.Id == id).Kind == WeaponKind.Melee)))
            throw new RuleViolation("Weapon mastery choices exceed eligibility or slots.");
        var weaponProficiencies = combat.Weapons.Where(w => WeaponProficient(w.Id,proficiencies,combat)).Select(w => w.Id).ToArray();
        var capabilities = new CombatCapabilities(speed,weaponProficiencies,resistance,[],[],[],attackCount,
            effects.Where(x => x.Effect.Kind == EffectKind.InitiativeBonus).Sum(x => x.Effect.Target == "proficiency" ? proficiencyBonus : x.Effect.Amount),
            effects.Any(x => x.Effect.Kind == EffectKind.InitiativeAdvantage), UntrainedArmorPenalty:untrainedArmor);
        var abilityBreakdowns = abilities.ToDictionary(x => x.Key,x => new DerivedStatistic(x.Value,
            new[] { new StatisticPart("Base",state.BaseAbilities[x.Key]),
                new StatisticPart($"background:{background.Id}",state.BackgroundBonuses.GetValueOrDefault(x.Key)),
                new StatisticPart("Advancement",state.AdvancementBonuses.GetValueOrDefault(x.Key)) }
                .Where(p => p.Value != 0).ToArray()));
        return new(character.Id,character.CampaignId,character.Name,character.Revision,"5.2.1",species.Id,variant?.Id,state.Size,
            background.Id,state.Classes,level,proficiencyBonus,abilities,modifiers,saves,skills,
            character.Health.State.Maximum,character.Health.State.Current,character.Health.State.Temporary,state.HitDice,
            armorClass,speed,proficiencies.ToArray(),features.ToArray(),state.FeatIds,state.Resources, state.Inventory,
            state.MasteredWeaponIds,capabilities,state.SubclassIds,abilityBreakdowns,new(speed,speedParts.ToArray()),
            untrainedArmor,untrainedArmor,darkvision,SpellSlotCalculator.Derive(character,state),state.PreparedSpells ?? [],
            state.SpellPackVersion ?? SpellPackVersions.Initial,state.KnownCantrips ?? [],state.WizardSpellbookIds ?? [],
            state.MetamagicOptions ?? [],SorceryPoints.Remaining(state),SorceryPoints.Maximum(state),
            FeatureSpells.AlwaysPrepared(state),state.MysticArcanumChoices ?? [],
            state.MysticArcanumSpentLevels ?? [],state.ArcaneRecoveryUsed,
            state.SorcerousRestorationUsed);
    }

    public static ItemDefinition FindItem(string id, CharacterRules rules, CombatContent combat) =>
        rules.Items.SingleOrDefault(x => x.Id == id) ??
        ((rules.ToolIds ?? []).Contains(id) ? new ItemDefinition(id,id,ItemKind.Gear) : null) ??
        (id == "black-pearl-powder-500gp" ? new ItemDefinition(id,"Crushed black pearl powder (500+ GP)",ItemKind.Gear) : null) ??
        (combat.Weapons.Any(x => x.Id == id) ? new ItemDefinition(id,id,ItemKind.Weapon) : throw new RuleViolation("Unknown item definition."));
    public static bool WeaponProficient(string id, IReadOnlyList<Proficiency> proficiencies, CombatContent combat)
    {
        var weapon = combat.Weapons.SingleOrDefault(x => x.Id == id) ?? throw new RuleViolation("Unknown weapon.");
        var ids = proficiencies.Where(x => x.Kind == ProficiencyKind.Weapon).Select(x => x.Id).ToHashSet();
        return ids.Contains(id) || ids.Contains(weapon.Category.ToString().ToLowerInvariant()) ||
            weapon.Category == WeaponCategory.Martial && weapon.Has(WeaponProperty.Light) && ids.Contains("martial-light") ||
            weapon.Category == WeaponCategory.Martial && (weapon.Has(WeaponProperty.Light) || weapon.Has(WeaponProperty.Finesse)) && ids.Contains("martial-finesse-or-light");
    }
    private static DerivedStatistic Statistic(int modifier, string ability, int proficiency) =>
        new(modifier+proficiency,proficiency == 0 ? [new(ability,modifier)] : [new(ability,modifier),new("Proficiency",proficiency)]);
}
