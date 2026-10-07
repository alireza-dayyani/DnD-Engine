using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public sealed class ProgressionService(ICampaignStore campaigns, IProgressionStore progressions,
    ICharacterRulesCatalog characterCatalog, ICombatCatalog combatCatalog, ICombatStore combatStore,
    ISpellCatalog spellCatalog, IDiceRoller dice, TimeProvider clock)
{
    public async Task<CharacterRules> ChoicesAsync(CancellationToken ct = default) =>
        await characterCatalog.GetAsync(Ruleset.Current,ct);

    public Task<SpellDefinition[]> SpellChoicesAsync(string? packVersion = null, CancellationToken ct = default) =>
        spellCatalog.GetAsync(Ruleset.Current,packVersion ?? SpellPackVersions.Current,ct);

    public async Task<CharacterSheet> CreateAsync(CreateSrdCharacter request, CancellationToken ct = default)
    {
        var campaign = await campaigns.GetCampaignAsync(request.CampaignId,ct) ?? throw new NotFoundException("Campaign not found.");
        campaign.Ruleset.RequireSupported();
        var rules = await characterCatalog.GetAsync(campaign.Ruleset,ct);
        var combat = await combatCatalog.GetAsync(campaign.Ruleset,ct);
        var spells = await spellCatalog.GetAsync(campaign.Ruleset,SpellPackVersions.Current,ct);
        var species = rules.Species.SingleOrDefault(x => x.Id == request.SpeciesId) ?? throw new RuleViolation("Unknown species.");
        var background = rules.Backgrounds.SingleOrDefault(x => x.Id == request.BackgroundId) ?? throw new RuleViolation("Unknown background.");
        var @class = rules.Classes.SingleOrDefault(x => x.Id == request.ClassId) ?? throw new RuleViolation("Unknown class.");
        if (request.BaseAbilities is null || request.BackgroundBonuses is null || request.ClassSkills is null)
            throw new RuleViolation("Abilities, background bonuses, and class skills are required.");
        if (request.BaseAbilities.Count != 6 || Enum.GetValues<Ability>().Any(a => !request.BaseAbilities.ContainsKey(a)))
            throw new RuleViolation("Exactly six base ability scores are required.");
        if (Enum.GetValues<Ability>().Any(a => request.BaseAbilities[a] is < 1 or > 20 ||
            request.BaseAbilities[a] + request.BackgroundBonuses.GetValueOrDefault(a) > 20))
            throw new RuleViolation("Creation ability scores must be between 1 and 20 after background bonuses.");
        if (species.Id == "human" && request.HumanOriginFeat is null || species.Id != "human" && request.HumanOriginFeat is not null)
            throw new RuleViolation("Human characters must choose one extra Origin feat.");
        var feats = new List<string> { background.OriginFeat };
        if (request.HumanOriginFeat is not null)
        {
            var extra = rules.Feats.SingleOrDefault(x => x.Id == request.HumanOriginFeat && x.Kind == FeatKind.Origin)
                ?? throw new RuleViolation("Human extra feat must be an SRD Origin feat.");
            if (extra.Id == background.OriginFeat && !extra.Repeatable) throw new RuleViolation("Origin feat cannot be selected twice.");
            feats.Add(extra.Id);
        }
        if (@class.Features.Any(x => x.Id == "fighting-style" && x.Level == 1))
        {
            var style = rules.Feats.SingleOrDefault(x => x.Id == request.FightingStyleFeat && x.Kind == FeatKind.FightingStyle)
                ?? throw new RuleViolation("Starting class requires a Fighting Style feat choice.");
            feats.Add(style.Id);
        }
        else if (request.FightingStyleFeat is not null) throw new RuleViolation("This class does not grant a starting Fighting Style.");
        var items = (request.StartingItemIds ?? []).Select(id =>
        {
            CharacterDeriver.FindItem(id,rules,combat);
            if (!@class.StartingEquipment.Contains(id) && !background.Equipment.Contains(id))
                throw new RuleViolation($"{id} is not in the selected starting equipment packages.");
            return new InventoryItem(Guid.NewGuid(),id);
        }).ToArray();
        var state = new ProgressionState(species.Id,request.SpeciesVariantId,request.Size,background.Id,
            new(request.BaseAbilities),new(request.BackgroundBonuses),new(),[new(@class.Id,1)],request.ClassSkills,
            feats.ToArray(),1,[new(@class.HitDie,1,1)],[],items,request.MasteredWeaponIds ?? [],request.SpeciesSkill);
        var expectedFeatChoices = feats.Sum(id => rules.Feats.Single(f => f.Id == id).ProficiencyChoiceCount);
        var featProficiencies = FeatProficiencies(request.FeatProficiencies,expectedFeatChoices,"feat:skilled",rules,
            [..background.Skills.Select(x => new Proficiency(ProficiencyKind.Skill,x,"background")),
             ..request.ClassSkills.Select(x => new Proficiency(ProficiencyKind.Skill,x,"class"))]);
        var constitution = new AbilityScore(request.BaseAbilities[Ability.Constitution] + request.BackgroundBonuses.GetValueOrDefault(Ability.Constitution)).Modifier;
        var speciesHp = species.Features.SelectMany(x => x.Effects).Where(x => x.Kind == EffectKind.HitPointsPerLevel).Sum(x => x.Amount);
        var maxHp = Math.Max(1,@class.HitDie + constitution + speciesHp);
        if (@class.Id != "wizard" && (request.WizardSpellbookIds ?? []).Length > 0)
            throw new RuleViolation("Only a Wizard starts with a spellbook.");
        var startingSpellbook = @class.Id == "wizard"
            ? WizardSpellbook.Add([],new ClassLevel("wizard",1),spells,request.WizardSpellbookIds ?? [],6)
            : [];
        state = state with { MaximumHp = maxHp, Resources = ResourcesFor(state,rules,[]),
            BackgroundToolId=request.BackgroundToolId, ClassTools=request.ClassTools ?? [],ExtraProficiencies=featProficiencies,
            PreparedSpells=ValidateStartingSpells(@class.Id,request.PreparedSpellIds,spells,startingSpellbook),
            KnownCantrips=CantripKnowledge.Add([],new ClassLevel(@class.Id,1),spells,request.KnownCantripIds ?? []),
            WizardSpellbookIds=startingSpellbook,
            SpellPackVersion=SpellPackVersions.Current };
        var character = Materialize(Guid.NewGuid(),campaign.Id,request.Name,state,rules,combat,new HitPoints(maxHp),0);
        var sheet = CharacterDeriver.Derive(character,state,rules,combat);
        var profile = Profile(character.Id,sheet,combat,null);
        await progressions.CreateAsync(character,state,profile,Event(character,"CharacterCreated",0,new { sheet }),ct);
        return sheet;
    }

    public async Task<CharacterSheet> SheetAsync(Guid id, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        return CharacterDeriver.Derive(character,state,rules,combat);
    }

    public async Task<SpellcastingSummary?> SpellcastingAsync(Guid id, CancellationToken ct = default) =>
        (await SheetAsync(id,ct)).Spellcasting;

    public async Task<CharacterSheet> LevelUpAsync(Guid id, LevelUpCharacter request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        if (character.Level.Value >= 20) throw new RuleViolation("Character level cannot exceed 20.");
        var next = rules.Classes.SingleOrDefault(x => x.Id == request.ClassId) ?? throw new RuleViolation("Unknown class.");
        var currentClass = state.Classes.SingleOrDefault(x => x.ClassId == next.Id);
        var newClass = currentClass is null;
        var oldSheet = CharacterDeriver.Derive(character,state,rules,combat);
        if (newClass && state.Classes.Select(x => rules.Classes.Single(c => c.Id == x.ClassId)).Append(next)
            .Any(c => c.AnyPrimaryAbility ? c.PrimaryAbilities.All(a => oldSheet.Abilities[a] < 13) :
                c.PrimaryAbilities.Any(a => oldSheet.Abilities[a] < 13)))
            throw new RuleViolation("Multiclassing requires 13 in every primary ability of each class.");
        var classLevel = (currentClass?.Level ?? 0) + 1;
        var classes = newClass ? [..state.Classes,new ClassLevel(next.Id,1)] :
            state.Classes.Select(x => x.ClassId == next.Id ? x with { Level = classLevel } : x).ToArray();
        var subclassIds = new Dictionary<string,string>(state.SubclassIds ?? []);
        if (classLevel == 3)
        {
            var subclass = (next.Subclasses ?? []).SingleOrDefault(x => x.Id == request.SubclassId)
                ?? throw new RuleViolation("This class level requires a valid SRD subclass choice.");
            subclassIds[next.Id] = subclass.Id;
        }
        else if (request.SubclassId is not null) throw new RuleViolation("This level does not grant a subclass choice.");
        var extraProficiencies = (state.ExtraProficiencies ?? []).ToList();
        if (newClass && next.MulticlassSkillCount > 0)
        {
            var skill = request.MulticlassSkill ?? throw new RuleViolation("This multiclass requires a skill choice.");
            if (!next.SkillChoices.Contains("*") && !next.SkillChoices.Contains(skill) ||
                oldSheet.Proficiencies.Any(x => x.Kind == ProficiencyKind.Skill && x.Id == skill))
                throw new RuleViolation("Invalid or duplicate multiclass skill choice.");
            extraProficiencies.Add(new(ProficiencyKind.Skill,skill,$"class:{next.Id}"));
        }
        else if (request.MulticlassSkill is not null) throw new RuleViolation("This level does not grant a multiclass skill choice.");
        if (newClass) foreach (var tool in next.MulticlassTools ?? [])
            extraProficiencies.Add(new(ProficiencyKind.Tool,tool,$"class:{next.Id}"));
        if (newClass && next.MulticlassToolChoiceCount > 0)
        {
            var tool = request.MulticlassTool ?? throw new RuleViolation("This multiclass requires a tool choice.");
            if (!(next.ToolChoiceOptions ?? []).Contains(tool) ||
                oldSheet.Proficiencies.Any(x => x.Kind == ProficiencyKind.Tool && x.Id == tool))
                throw new RuleViolation("Invalid or duplicate multiclass tool choice.");
            extraProficiencies.Add(new(ProficiencyKind.Tool,tool,$"class:{next.Id}"));
        }
        else if (request.MulticlassTool is not null) throw new RuleViolation("This level does not grant a multiclass tool choice.");
        var featLevel = next.Features.Any(f => f.Id is "ability-score-improvement" or "epic-boon" && f.Level == classLevel);
        var epicBoon = next.Features.Any(f => f.Id == "epic-boon" && f.Level == classLevel);
        var bonuses = new Dictionary<Ability,int>(state.AdvancementBonuses);
        var feats = state.FeatIds.ToList();
        if (next.Features.Any(x => x.Id == "fighting-style" && x.Level == classLevel))
        {
            var style = rules.Feats.SingleOrDefault(x => x.Id == request.FightingStyleFeat && x.Kind == FeatKind.FightingStyle)
                ?? throw new RuleViolation("This level requires a Fighting Style feat choice.");
            if (feats.Contains(style.Id)) throw new RuleViolation("Fighting Style feat is not repeatable.");
            feats.Add(style.Id);
        }
        else if (request.FightingStyleFeat is not null) throw new RuleViolation("This level does not grant a Fighting Style.");
        if (featLevel)
        {
            if (request.FeatId is null) throw new RuleViolation("This level requires an Ability Score Improvement or eligible feat choice.");
            if (request.FeatId == "ability-score-improvement" && !epicBoon)
            {
                var increases = request.AbilityIncreases ?? throw new RuleViolation("Ability Score Improvement requires ability increases.");
                if (increases.Values.Order().SequenceEqual([2]) == false && increases.Values.Order().SequenceEqual([1,1]) == false)
                    throw new RuleViolation("Ability Score Improvement must be +2 or +1/+1.");
                foreach (var (ability,amount) in increases)
                {
                    if (oldSheet.Abilities[ability] + amount > 20) throw new RuleViolation("Ability Score Improvement cannot exceed 20.");
                    bonuses[ability] = bonuses.GetValueOrDefault(ability) + amount;
                }
                feats.Add("ability-score-improvement");
            }
            else
            {
                var feat = rules.Feats.SingleOrDefault(x => x.Id == request.FeatId &&
                    (epicBoon ? x.Kind == FeatKind.EpicBoon : x.Kind is FeatKind.General or FeatKind.Origin) &&
                    x.MinimumLevel <= character.Level.Value + 1)
                    ?? throw new RuleViolation("Feat is not eligible at this level.");
                if (!feat.Repeatable && feats.Contains(feat.Id)) throw new RuleViolation("Feat is not repeatable.");
                if (feat.AbilityPrerequisite is { } ability && oldSheet.Abilities[ability] < feat.MinimumAbility ||
                    feat.AnyAbilityPrerequisites is { Length: > 0 } options && options.All(a => oldSheet.Abilities[a] < feat.MinimumAbility))
                    throw new RuleViolation("Feat ability prerequisite is not met.");
                if (feat.AbilityBoostAmount > 0)
                {
                    var increase = request.AbilityIncreases ?? throw new RuleViolation("Feat requires an ability increase choice.");
                    if (increase.Count != 1 || increase.Values.Single() != feat.AbilityBoostAmount)
                        throw new RuleViolation("Invalid feat ability increase.");
                    var chosen = increase.Keys.Single();
                    if (!(feat.AbilityBoostOptions ?? []).Contains(chosen))
                        throw new RuleViolation("Ability is not eligible for this Epic Boon.");
                    if (oldSheet.Abilities[chosen]+feat.AbilityBoostAmount > feat.AbilityBoostCap)
                        throw new RuleViolation("Feat ability increase exceeds its cap.");
                    bonuses[chosen] = bonuses.GetValueOrDefault(chosen)+feat.AbilityBoostAmount;
                }
                else if (request.AbilityIncreases is not null) throw new RuleViolation("This feat grants no ability increase.");
                feats.Add(feat.Id);
            }
        }
        else if (request.FeatId is not null || request.AbilityIncreases is not null) throw new RuleViolation("This level does not grant a feat choice.");
        var selectedFeat = request.FeatId is null ? null : rules.Feats.Single(f => f.Id == request.FeatId);
        var extraChoices = FeatProficiencies(request.FeatProficiencies,selectedFeat?.ProficiencyChoiceCount ?? 0,
            request.FeatId is null ? "feat" : $"feat:{request.FeatId}",rules,oldSheet.Proficiencies);
        extraProficiencies.AddRange(extraChoices);
        var prepared = await ReplaceSpellsAsync(character,state,classes,
            request.SpellReplacement is null ? [] : [request.SpellReplacement],
            SpellPreparationMoment.ClassLevelGained,next.Id,ct);
        var additional = request.AdditionalPreparedSpellIds ?? [];
        var additionalCantrips = request.AdditionalCantripIds ?? [];
        var additionalBook = request.AdditionalWizardSpellbookIds ?? [];
        var arcanum = state.MysticArcanumChoices ?? [];
        var additionalMetamagic = request.AdditionalMetamagicOptions ?? [];
        var metamagic = state.MetamagicOptions ?? [];
        if (additionalMetamagic.Length > 0)
        {
            if (next.Id != "sorcerer" || classLevel is not (2 or 10 or 17) ||
                additionalMetamagic.Length > 2 ||
                additionalMetamagic.Any(x => !Enum.IsDefined(x) || metamagic.Contains(x)) ||
                additionalMetamagic.Distinct().Count() != additionalMetamagic.Length)
                throw new RuleViolation("Metamagic choices are unavailable or duplicate at this level.");
            metamagic = [..metamagic,..additionalMetamagic];
        }
        var spellbook = state.WizardSpellbookIds ?? [];
        var cantrips = state.KnownCantrips ?? [];
        if (additional.Length > 0 || additionalCantrips.Length > 0 || additionalBook.Length > 0 ||
            request.MysticArcanumSpellId is not null ||
            request.CantripReplacement is not null)
        {
            var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct)
                ?? throw new NotFoundException("Campaign not found.");
            var catalog = await spellCatalog.GetAsync(campaign.Ruleset,
                state.SpellPackVersion ?? SpellPackVersions.Initial,ct);
            if (additionalBook.Length > 0)
                spellbook = WizardSpellbook.Add(spellbook,new ClassLevel(next.Id,classLevel),catalog,
                    additionalBook,newClass ? 6 : 2);
            prepared = SpellPreparation.Add(prepared,new ClassLevel(next.Id,classLevel),catalog,additional,spellbook);
            cantrips = CantripKnowledge.Replace(cantrips,classes,catalog,request.CantripReplacement,next.Id,false);
            cantrips = CantripKnowledge.Add(cantrips,new ClassLevel(next.Id,classLevel),catalog,additionalCantrips);
            if (request.MysticArcanumSpellId is not null)
            {
                if (next.Id != "warlock") throw new RuleViolation("Only Warlock gains Mystic Arcanum.");
                arcanum = MysticArcanum.Choose(arcanum,classLevel,request.MysticArcanumSpellId,catalog);
            }
        }
        int dieResult;
        if (request.HpMethod.Equals("Fixed",StringComparison.OrdinalIgnoreCase)) dieResult = next.HitDie / 2 + 1;
        else if (request.HpMethod.Equals("Roll",StringComparison.OrdinalIgnoreCase)) dieResult = dice.Roll(new(1,next.HitDie)).Total;
        else throw new RuleViolation("HP method must be Fixed or Roll.");
        var updated = state with { Classes = classes, FeatIds = feats.ToArray(), AdvancementBonuses = bonuses,
            ExtraProficiencies = extraProficiencies.ToArray(), SubclassIds = subclassIds,
            PreparedSpells=prepared, KnownCantrips=cantrips,WizardSpellbookIds=spellbook,
            MetamagicOptions=metamagic,MysticArcanumChoices=arcanum };
        var newCon = new AbilityScore(updated.BaseAbilities[Ability.Constitution] + updated.BackgroundBonuses.GetValueOrDefault(Ability.Constitution) + bonuses.GetValueOrDefault(Ability.Constitution)).Modifier;
        var oldCon = oldSheet.AbilityModifiers[Ability.Constitution];
        var species = rules.Species.Single(x => x.Id == state.SpeciesId);
        var extraPerLevel = species.Features.SelectMany(x => x.Effects).Where(x => x.Kind == EffectKind.HitPointsPerLevel).Sum(x => x.Amount);
        var hpGain = Math.Max(1,dieResult+newCon+extraPerLevel) + character.Level.Value*(newCon-oldCon);
        var pools = state.HitDice.Select(x => x.Sides == next.HitDie ? x with { Total = x.Total+1, Available = x.Available+1 } : x).ToList();
        if (pools.All(x => x.Sides != next.HitDie)) pools.Add(new(next.HitDie,1,1));
        updated = updated with { HitDice = pools.ToArray(), MaximumHp = state.MaximumHp+hpGain };
        updated = updated with { Resources = ResourcesFor(updated,rules,state.Resources) };
        var oldHealth = character.Health.State;
        var health = new HitPoints(oldHealth with { Maximum = updated.MaximumHp,
            Current = Math.Clamp(oldHealth.Current+hpGain,0,updated.MaximumHp) });
        var nextCharacter = Materialize(character.Id,character.CampaignId,character.Name,updated,rules,combat,health,character.Revision);
        var sheet = CharacterDeriver.Derive(nextCharacter,updated,rules,combat);
        var profile = Profile(id,sheet,combat,await combatStore.GetProfileAsync(id,ct));
        await progressions.SaveAsync(nextCharacter,updated,profile,Event(character,"LevelGained",character.Revision+1,
            new { classId=next.Id,classLevel,totalLevel=sheet.Level,hpGain,feat=request.FeatId,
                spellReplacement=request.SpellReplacement,additionalPreparedSpellIds=additional,
                cantripReplacement=request.CantripReplacement,additionalCantripIds=additionalCantrips,
                additionalWizardSpellbookIds=additionalBook,additionalMetamagicOptions=additionalMetamagic,
                mysticArcanumSpellId=request.MysticArcanumSpellId }),ct);
        return sheet with { Revision = character.Revision+1 };
    }

    public async Task<CharacterSheet> AcquireItemAsync(Guid id, ItemChange request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        CharacterDeriver.FindItem(request.DefinitionId,rules,combat);
        var item = new InventoryItem(Guid.NewGuid(),request.DefinitionId);
        return await Save(character,state with { Inventory=[..state.Inventory,item] },rules,combat,"ItemAcquired",new { item },ct);
    }
    public async Task<CharacterSheet> EquipAsync(Guid id, EquipItem request, bool equipped, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        var item = state.Inventory.SingleOrDefault(x => x.Id == request.ItemId) ?? throw new RuleViolation("Item is not owned.");
        var definition = CharacterDeriver.FindItem(item.DefinitionId,rules,combat);
        if (definition.Kind == ItemKind.Gear) throw new RuleViolation("This gear has no equipment slot.");
        var inventory = state.Inventory.Select(x => x.Id == item.Id ? x with { Equipped=equipped } :
            equipped && definition.Kind is ItemKind.Armor or ItemKind.Shield &&
            CharacterDeriver.FindItem(x.DefinitionId,rules,combat).Kind == definition.Kind ? x with { Equipped=false } : x).ToArray();
        return await Save(character,state with { Inventory=inventory },rules,combat,equipped ? "ItemEquipped" : "ItemUnequipped",new { itemId=item.Id },ct);
    }
    public async Task<CharacterSheet> RemoveItemAsync(Guid id, EquipItem request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        if (!state.Inventory.Any(x => x.Id == request.ItemId)) throw new RuleViolation("Item is not owned.");
        return await Save(character,state with { Inventory=state.Inventory.Where(x => x.Id != request.ItemId).ToArray() },rules,combat,"ItemRemoved",new { itemId=request.ItemId },ct);
    }

    public async Task<CharacterSheet> SpendResourceAsync(Guid id, SpendResource request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        Guard.Range(request.Amount,1,1000000,"Resource amount");
        var resource = state.Resources.SingleOrDefault(x => x.Id == request.ResourceId) ?? throw new RuleViolation("Resource not available.");
        if (resource.Current < request.Amount) throw new RuleViolation("Insufficient resource uses.");
        var resources = state.Resources.Select(x => x.Id == resource.Id ? x with { Current=x.Current-request.Amount } : x).ToArray();
        return await Save(character,state with { Resources=resources },rules,combat,"ResourceChanged",
            new { resourceId=resource.Id, before=resource.Current, after=resource.Current-request.Amount },ct);
    }

    public async Task<CharacterSheet> SpendSpellSlotAsync(Guid id, SpendSpellSlot request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        var (usage,before) = SpellSlotCalculator.Spend(character,state,request.Pool,request.SpellLevel);
        var updated = state with { SpellSlots=usage };
        return await Save(character,updated,rules,combat,"SpellSlotSpent",
            new { pool=request.Pool,spellLevel=request.SpellLevel,before,after=before-1 },ct);
    }

    public async Task<CharacterSheet> ConvertSpellSlotAsync(Guid id,ConvertSpellSlot request,CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        var before = SorceryPoints.Remaining(state);
        var updated = SorceryPoints.ConvertSlot(character,state,request.Pool,request.SpellLevel);
        return await Save(character,updated,rules,combat,"SpellSlotConverted",
            new { request.Pool,request.SpellLevel,pointsBefore=before,pointsAfter=SorceryPoints.Remaining(updated) },ct);
    }

    public async Task<CharacterSheet> CreateSorcerySlotAsync(Guid id,CreateSorcerySlot request,CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        var before = SorceryPoints.Remaining(state);
        var updated = SorceryPoints.CreateSlot(state,request.SpellLevel);
        return await Save(character,updated,rules,combat,"SorcerySlotCreated",
            new { request.SpellLevel,pointsBefore=before,pointsAfter=SorceryPoints.Remaining(updated) },ct);
    }

    public async Task<SpellCastResult> CastPreparedSpellAsync(Guid id, CastPreparedSpell request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        if (!request.ComponentsAvailable) throw new RuleViolation("Verbal and somatic spell components must be available.");
        character.Health.RequireAlive();
        if (character.Health.State.Unconscious) throw new RuleViolation("An unconscious character cannot cast a spell.");
        var profile = await combatStore.GetProfileAsync(id,ct) ?? throw new RuleViolation("Combat profile is missing.");
        new ConditionEffects(character,profile).RequireAction();
        var sheet = CharacterDeriver.Derive(character,state,rules,combat);
        if (sheet.SpellcastingBlockedByArmor) throw new RuleViolation("Untrained armor prevents spellcasting.");
        var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct) ?? throw new NotFoundException("Campaign not found.");
        var spell = (await spellCatalog.GetAsync(campaign.Ruleset,state.SpellPackVersion ?? SpellPackVersions.Initial,ct))
            .SingleOrDefault(x => x.Id == request.SpellId)
            ?? throw new RuleViolation("Spell is not in the installed catalog.");
        if (!(state.PreparedSpells ?? []).Any(x => x.ClassId == request.ClassId && x.SpellId == spell.Id) ||
            !spell.ClassIds.Contains(request.ClassId))
            throw new RuleViolation("Spell is not prepared for this class.");
        if (request.SpellLevel < spell.Level) throw new RuleViolation("Spell slot is below the spell's level.");
        var casting = sheet.Spellcasting?.Classes.SingleOrDefault(x => x.ClassId == request.ClassId)
            ?? throw new RuleViolation("Class has no spellcasting feature.");
        if (spell.Effect != SpellEffectKind.SelfHealing || spell.DicePerSlotLevel <= 0 || spell.DieSides <= 0)
            throw new RuleViolation("Spell effect is not implemented.");
        var (usage,before) = SpellSlotCalculator.Spend(character,state,request.Pool,request.SpellLevel);
        var rolls = dice.Roll(new(spell.DicePerSlotLevel*request.SpellLevel,spell.DieSides));
        var healing = Math.Max(0,rolls.Total+casting.AbilityModifier);
        var change = character.Health.Heal(healing);
        var updated = state with { SpellSlots=usage };
        var resultSheet = await Save(character,updated,rules,combat,"SpellCast",new {
            spellId=spell.Id,classId=request.ClassId,pool=request.Pool,slotLevel=request.SpellLevel,
            targetCharacterId=id,componentsAvailable=request.ComponentsAvailable,slotBefore=before,slotAfter=before-1,
            rolls=rolls.Rolls,abilityModifier=casting.AbilityModifier,health=change },ct);
        return new(resultSheet,request.ClassId,spell.Id,request.Pool,request.SpellLevel,
            rolls.Rolls.ToArray(),casting.AbilityModifier,change.HitPointsRegained);
    }

    public async Task<LanguageSpellCastResult> CastLanguageSpellAsync(Guid id,
        CastLanguageSpell request,CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedCharacterRevision,ct);
        var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        if (campaign.Revision != request.ExpectedCampaignRevision)
            throw new StateConflictException("Campaign time changed. Reload before casting.");
        if (await campaigns.HasUnfinishedEncounterAsync(campaign.Id,ct))
            throw new RuleViolation("Long casting cannot proceed during an unfinished encounter.");
        var spell = (await spellCatalog.GetAsync(campaign.Ruleset,
            state.SpellPackVersion ?? SpellPackVersions.Initial,ct))
            .SingleOrDefault(x => x.Id == "comprehend-languages")
            ?? throw new RuleViolation("Comprehend Languages is not in the pinned spell pack.");
        if (!spell.ClassIds.Contains(request.ClassId) ||
            spell.Effect != SpellEffectKind.LanguageComprehension)
            throw new RuleViolation("Class cannot cast this language spell.");
        var prepared = (state.PreparedSpells ?? []).Any(x =>
            x.ClassId == request.ClassId && x.SpellId == spell.Id);
        var wizardBookRitual = request.Ritual && request.ClassId == "wizard" &&
            (state.WizardSpellbookIds ?? []).Contains(spell.Id);
        if (!prepared && !wizardBookRitual)
            throw new RuleViolation("The spell must be prepared, or read from a Wizard spellbook as a ritual.");
        if (!prepared && wizardBookRitual && !request.SpellbookAvailable)
            throw new RuleViolation("The Wizard must read the spell from the available spellbook.");
        if (request.Ritual && !request.Uninterrupted)
            throw new RuleViolation("Ritual casting requires an uninterrupted long casting period.");
        if (!request.VerbalAvailable || !request.SomaticAvailable || !request.MaterialAvailable)
            throw new RuleViolation("Comprehend Languages requires verbal, somatic and soot-and-salt material components.");
        var sheet = CharacterDeriver.Derive(character,state,rules,combat);
        if (sheet.SpellcastingBlockedByArmor)
            throw new RuleViolation("Untrained armor prevents spellcasting.");
        var profile = await combatStore.GetProfileAsync(id,ct)
            ?? throw new RuleViolation("Combat profile is missing.");
        new ConditionEffects(character,profile).RequireAction();
        character.Health.RequireAlive();
        if (SpellSlotCalculator.Derive(character,state)?.Classes.Any(x => x.ClassId == request.ClassId) != true)
            throw new RuleViolation("Class has no spellcasting feature.");
        SpellSlotUsage? usage = null; int? slotBefore = null;
        if (request.Ritual)
        {
            if (!spell.Ritual || request.Pool is not null || request.SpellLevel != spell.Level)
                throw new RuleViolation("A ritual cannot use a slot or a higher spell level.");
        }
        else
        {
            if (request.Pool is null || request.SpellLevel < spell.Level)
                throw new RuleViolation("A normal cast requires an eligible spell slot.");
            (usage,slotBefore) = SpellSlotCalculator.Spend(character,state,request.Pool.Value,request.SpellLevel);
        }
        var after = campaign.AdvanceTime(request.Ritual ? 606 : 6);
        var expires = checked(after.GameSeconds+3600);
        var updated = state with { SpellSlots=usage ?? state.SpellSlots,
            ComprehendLanguagesUntilGameSecond=expires };
        var nextCharacter = Materialize(character.Id,character.CampaignId,character.Name,
            updated,rules,combat,character.Health,character.Revision);
        var resultSheet = CharacterDeriver.Derive(nextCharacter,updated,rules,combat);
        var nextProfile = Profile(character.Id,resultSheet,combat,profile);
        await progressions.SaveWithCampaignTimeAsync(nextCharacter,updated,nextProfile,campaign,after,
            Event(character,"LanguageSpellCast",character.Revision+1,new {
                spellId=spell.Id,request.ClassId,request.Ritual,request.Pool,request.SpellLevel,
                slotBefore,completedAtGameSecond=after.GameSeconds,expiresAtGameSecond=expires }),ct);
        return new(resultSheet with { Revision=character.Revision+1 },spell.Id,request.Ritual,
            request.Pool,slotBefore,after.GameSeconds,expires,after.Revision);
    }

    public async Task<LanguageComprehensionResult> CheckLanguageComprehensionAsync(Guid id,
        CheckLanguageComprehension request,CancellationToken ct = default)
    {
        Guard.Defined(request.Medium);
        var character = await campaigns.GetCharacterAsync(id,ct)
            ?? throw new NotFoundException("Character not found.");
        var state = await progressions.GetAsync(id,ct)
            ?? throw new RuleViolation("Character has no spellcasting progression.");
        var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        if (state.ComprehendLanguagesUntilGameSecond < 0)
            throw new RuleViolation("Invalid language spell duration.");
        var active = state.ComprehendLanguagesUntilGameSecond > campaign.GameSeconds;
        var understands = active && request.Perceived &&
            (request.Medium != LanguageMedium.Written || request.TouchingSurface);
        return new(request.Medium,active,understands,campaign.GameSeconds,
            state.ComprehendLanguagesUntilGameSecond);
    }

    public async Task<CharacterSheet> AdoptSpellPackAsync(Guid id, AdoptSpellPack request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        if (request.PackVersion != SpellPackVersions.Current ||
            state.SpellPackVersion == SpellPackVersions.Current)
            throw new RuleViolation("Spell pack adoption must move to the current pack.");
        var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        var catalog = await spellCatalog.GetAsync(campaign.Ruleset,request.PackVersion,ct);
        foreach (var choice in state.PreparedSpells ?? [])
            if (!catalog.Any(x => x.Id == choice.SpellId && x.ClassIds.Contains(choice.ClassId)))
                throw new RuleViolation("New spell pack omits a prepared spell.");
        foreach (var idInBook in state.WizardSpellbookIds ?? [])
            if (!catalog.Any(x => x.Id == idInBook && x.Level > 0 && x.ClassIds.Contains("wizard")))
                throw new RuleViolation("New spell pack omits a spellbook spell.");
        foreach (var (_,arcanumId) in state.MysticArcanumChoices ?? [])
            if (!catalog.Any(x => x.Id == arcanumId && x.ClassIds.Contains("warlock")))
                throw new RuleViolation("New spell pack omits a Mystic Arcanum spell.");
        var cantrips = state.KnownCantrips ?? [];
        foreach (var group in (request.KnownCantrips ?? []).GroupBy(x => x.ClassId))
        {
            var classLevel = state.Classes.SingleOrDefault(x => x.ClassId == group.Key)
                ?? throw new RuleViolation("Cantrip class is not owned.");
            cantrips = CantripKnowledge.Add(cantrips,classLevel,catalog,group.Select(x => x.SpellId).ToArray());
        }
        return await Save(character,state with { SpellPackVersion=request.PackVersion,KnownCantrips=cantrips },
            rules,combat,"SpellPackAdopted",new { from=state.SpellPackVersion ?? SpellPackVersions.Initial,
                to=request.PackVersion,knownCantrips=request.KnownCantrips ?? [] },ct);
    }

    public async Task<RestResult> ShortRestAsync(Guid id, ShortRestRequest request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        character.Health.RequireAlive();
        if (character.Health.State.Current < 1) throw new RuleViolation("Short Rest requires at least 1 HP.");
        var pools = state.HitDice.ToArray();
        var rolls = new List<int>();
        var before = character.Health.State.Current;
        foreach (var sides in request.HitDieSides ?? [])
        {
            var index = Array.FindIndex(pools,x => x.Sides == sides && x.Available > 0);
            if (index < 0) throw new RuleViolation("Requested Hit Die is unavailable.");
            var rolled = dice.Roll(new(1,sides)).Total;
            rolls.Add(rolled);
            pools[index] = pools[index] with { Available=pools[index].Available-1 };
            character.Health.Heal(Math.Max(1,rolled+character.AbilityModifier(Ability.Constitution)));
        }
        var resources = state.Resources.Select(x => x with { Current=x.Recovery switch {
            RecoveryKind.ShortRest => x.Maximum, RecoveryKind.OneOnShortRest => Math.Min(x.Maximum,x.Current+1), _ => x.Current } }).ToArray();
        var recoveredLevels = request.ArcaneRecoverySlotLevels ?? [];
        var wizardLevel = state.Classes.SingleOrDefault(x => x.ClassId == "wizard")?.Level ?? 0;
        if (recoveredLevels.Length > 0 && (wizardLevel == 0 || state.ArcaneRecoveryUsed))
            throw new RuleViolation("Arcane Recovery is unavailable until the next Long Rest.");
        var slotUsage = recoveredLevels.Length == 0 ? state.SpellSlots :
            SpellSlotCalculator.RecoverShared(character,state,recoveredLevels,(wizardLevel+1)/2);
        Guard.Range(request.SorceryPointsToRestore,0,20,"Sorcery Points to restore");
        var sorcererLevel = state.Classes.SingleOrDefault(x => x.ClassId == "sorcerer")?.Level ?? 0;
        if (request.SorceryPointsToRestore > 0 && (sorcererLevel < 5 || state.SorcerousRestorationUsed ||
            request.SorceryPointsToRestore > sorcererLevel/2 ||
            request.SorceryPointsToRestore > state.SorceryPointsSpent))
            throw new RuleViolation("Sorcerous Restoration is unavailable or exceeds its allowance.");
        var prepared = state.PreparedSpells ?? [];
        if (request.MemorizeSpell is { } replacement)
        {
            if (wizardLevel < 5 || replacement.ClassId != "wizard")
                throw new RuleViolation("Memorize Spell requires Wizard level 5.");
            prepared = await ReplaceSpellsAsync(character,state,state.Classes,[replacement],
                SpellPreparationMoment.LongRest,null,ct);
        }
        var updated = state with { HitDice=pools, Resources=resources,
            SpellSlots=slotUsage is null ? null : slotUsage with { PactSpent=0 },
            ArcaneRecoveryUsed=state.ArcaneRecoveryUsed || recoveredLevels.Length > 0,
            SorceryPointsSpent=state.SorceryPointsSpent-request.SorceryPointsToRestore,
            SorcerousRestorationUsed=state.SorcerousRestorationUsed || request.SorceryPointsToRestore > 0,
            PreparedSpells=prepared };
        var sheet = await Save(character,updated,rules,combat,"RestCompleted",new { kind="Short",rolls,
            hpRegained=character.Health.State.Current-before,pactSlotsRestored=state.SpellSlots?.PactSpent ?? 0,
            arcaneRecoverySlotLevels=recoveredLevels,sorceryPointsRestored=request.SorceryPointsToRestore,
            memorizeSpell=request.MemorizeSpell },ct);
        var changes = new List<string>();
        if (state.SpellSlots?.PactSpent > 0) changes.Add("Pact Magic slots restored");
        if (recoveredLevels.Length > 0) changes.Add("Arcane Recovery slots restored");
        if (request.SorceryPointsToRestore > 0) changes.Add("Sorcery Points restored");
        if (request.MemorizeSpell is not null) changes.Add("Wizard spell memorized");
        return new(sheet,rolls.ToArray(),character.Health.State.Current-before,
            resources.Where((x,i) => x.Current != state.Resources[i].Current).ToArray(),changes.ToArray());
    }
    public async Task<RestResult> LongRestAsync(Guid id, LongRestRequest request, CancellationToken ct = default)
    {
        var (character,state,rules,combat) = await Load(id,ct);
        await RequireMutable(character,request.ExpectedRevision,ct);
        character.Health.RequireAlive();
        if (character.Health.State.Current < 1) throw new RuleViolation("Long Rest requires at least 1 HP.");
        var now = clock.GetUtcNow();
        if (state.LastLongRestAtUtc is { } last && now-last < TimeSpan.FromHours(24))
            throw new RuleViolation("A new Long Rest cannot finish before the required 16-hour interval and 8-hour rest.");
        var replacements = request.SpellReplacements ?? [];
        var prepared = await ReplaceSpellsAsync(character,state,state.Classes,replacements,
            SpellPreparationMoment.LongRest,null,ct);
        var cantrips = state.KnownCantrips ?? [];
        if (request.CantripReplacement is not null)
        {
            var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct)
                ?? throw new NotFoundException("Campaign not found.");
            var catalog = await spellCatalog.GetAsync(campaign.Ruleset,
                state.SpellPackVersion ?? SpellPackVersions.Initial,ct);
            cantrips = CantripKnowledge.Replace(cantrips,state.Classes,catalog,request.CantripReplacement,null,true);
        }
        var before = character.Health.State.Current;
        var health = new HitPoints(character.Health.State with { Current=character.Health.State.Maximum, Temporary=0,Stable=false,
            DeathSuccesses=0,DeathFailures=0 });
        var updated = state with { HitDice=state.HitDice.Select(x => x with { Available=x.Total }).ToArray(),
            Resources=state.Resources.Select(x => x with { Current=x.Maximum }).ToArray(), LastLongRestAtUtc=now,
            MasteredWeaponIds=request.MasteredWeaponIds ?? state.MasteredWeaponIds, SpellSlots=null,
            PreparedSpells=prepared,KnownCantrips=cantrips,SorceryPointsSpent=0,
            MysticArcanumSpentLevels=[],ArcaneRecoveryUsed=false,SorcerousRestorationUsed=false };
        var nextCharacter = Materialize(character.Id,character.CampaignId,character.Name,updated,rules,combat,health,character.Revision);
        var sheet = CharacterDeriver.Derive(nextCharacter,updated,rules,combat);
        var oldProfile = await combatStore.GetProfileAsync(id,ct);
        var profile = Profile(id,sheet,combat,oldProfile);
        var exhaustion = profile.State.Conditions.FirstOrDefault(x => x.Kind == ConditionKind.Exhaustion);
        if (exhaustion is not null) profile.RemoveCondition(exhaustion.Id);
        var sharedRestored = state.SpellSlots?.SharedSpentByLevel.Sum() ?? 0;
        var pactRestored = state.SpellSlots?.PactSpent ?? 0;
        await progressions.SaveAsync(nextCharacter,updated,profile,Event(character,"RestCompleted",character.Revision+1,
            new { kind="Long",hpRegained=health.State.Current-before,exhaustionReduced=exhaustion is not null,
                sharedSlotsRestored=sharedRestored,pactSlotsRestored=pactRestored,
                spellReplacements=replacements,cantripReplacement=request.CantripReplacement }),ct);
        var changes = new List<string> { "Temporary HP expired" };
        if (exhaustion is not null) changes.Add("Exhaustion reduced by one");
        if (sharedRestored > 0 || pactRestored > 0) changes.Add("Spell slots restored");
        if (replacements.Length > 0) changes.Add("Prepared spells replaced");
        return new(sheet with { Revision=character.Revision+1 },[],health.State.Current-before,
            updated.Resources.Where((x,i) => x.Current != state.Resources[i].Current).ToArray(),changes.ToArray());
    }

    private async Task<CharacterSheet> Save(Character character, ProgressionState state, CharacterRules rules, CombatContent combat,
        string eventType, object data, CancellationToken ct)
    {
        var nextCharacter = Materialize(character.Id,character.CampaignId,character.Name,state,rules,combat,character.Health,character.Revision);
        var sheet = CharacterDeriver.Derive(nextCharacter,state,rules,combat);
        var profile = Profile(character.Id,sheet,combat,await combatStore.GetProfileAsync(character.Id,ct));
        await progressions.SaveAsync(nextCharacter,state,profile,Event(character,eventType,character.Revision+1,data),ct);
        return sheet with { Revision=character.Revision+1 };
    }
    private async Task<PreparedSpell[]> ReplaceSpellsAsync(Character character, ProgressionState state,
        ClassLevel[] classes, SpellReplacement[] replacements, SpellPreparationMoment moment,
        string? gainedClassId, CancellationToken ct)
    {
        var current = state.PreparedSpells ?? [];
        if (replacements.Length == 0) return current;
        var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        var catalog = await spellCatalog.GetAsync(campaign.Ruleset,
            state.SpellPackVersion ?? SpellPackVersions.Initial,ct);
        return SpellPreparation.Replace(current,classes,catalog,replacements,moment,gainedClassId,
            state.WizardSpellbookIds);
    }
    private async Task<(Character Character,ProgressionState State,CharacterRules Rules,CombatContent Combat)> Load(Guid id,CancellationToken ct)
    {
        var character = await campaigns.GetCharacterAsync(id,ct) ?? throw new NotFoundException("Character not found.");
        var state = await progressions.GetAsync(id,ct) ?? throw new RuleViolation("Legacy imported character has no progression choices; automated Phase 3 operations require a newly created SRD character.");
        var campaign = await campaigns.GetCampaignAsync(character.CampaignId,ct) ?? throw new NotFoundException("Campaign not found.");
        return (character,state,await characterCatalog.GetAsync(campaign.Ruleset,ct),await combatCatalog.GetAsync(campaign.Ruleset,ct));
    }
    private async Task RequireMutable(Character character,long expected,CancellationToken ct)
    {
        if (character.Revision != expected) throw new StateConflictException("Character revision changed. Reload before trying again.");
        if (await progressions.IsEnrolledAsync(character.Id,ct)) throw new StateConflictException("Character is in an active encounter.");
    }
    private static ResourceState[] ResourcesFor(ProgressionState state,CharacterRules rules,ResourceState[] previous)
    {
        var result = new List<ResourceState>();
        foreach (var allocation in state.Classes)
        {
            var definition = rules.Classes.Single(x => x.Id == allocation.ClassId);
            foreach (var feature in definition.Features.Where(f => f.Level <= allocation.Level))
            foreach (var effect in feature.Effects.Where(e => e.Kind == EffectKind.Resource))
            {
                var maximum = effect.Values is { } values ? values[allocation.Level-1] : effect.ScalingAbility is { } ability ?
                    Math.Max(effect.Amount,new AbilityScore(state.BaseAbilities[ability] + state.BackgroundBonuses.GetValueOrDefault(ability) +
                        state.AdvancementBonuses.GetValueOrDefault(ability)).Modifier) : effect.Amount;
                if (maximum <= 0 || result.Any(x => x.Id == effect.Target)) continue;
                var old = previous.SingleOrDefault(x => x.Id == effect.Target);
                result.Add(new(effect.Target,$"class:{definition.Id}",old is null ? maximum : Math.Min(maximum,old.Current),maximum,effect.Recovery));
            }
        }
        foreach (var allocation in state.Classes)
        {
            var definition = rules.Classes.Single(x => x.Id == allocation.ClassId);
            foreach (var effect in definition.Features.Where(f => f.Level <= allocation.Level)
                .SelectMany(f => f.Effects).Where(e => e.Kind == EffectKind.ResourceRecovery))
            {
                var index = result.FindIndex(x => x.Id == effect.Target);
                if (index >= 0) result[index] = result[index] with { Recovery=effect.Recovery };
            }
        }
        return result.ToArray();
    }
    private static Proficiency[] FeatProficiencies(Proficiency[]? selected,int expected,string source,CharacterRules rules,
        IReadOnlyList<Proficiency> existing)
    {
        var values = selected ?? [];
        if (values.Length != expected || values.Select(x => (x.Kind,x.Id)).Distinct().Count() != values.Length)
            throw new RuleViolation($"Feat requires exactly {expected} distinct proficiency choices.");
        foreach (var value in values)
        {
            if (value.Kind == ProficiencyKind.Skill ? !CharacterDeriver.IsSkillId(value.Id) :
                value.Kind == ProficiencyKind.Tool ? !(rules.ToolIds ?? []).Contains(value.Id) : true)
                throw new RuleViolation("Feat proficiency choice must be a canonical skill or tool.");
            if (existing.Any(x => x.Kind == value.Kind && x.Id == value.Id))
                throw new RuleViolation("Feat proficiency choice duplicates existing training.");
        }
        return values.Select(x => x with { Source=source }).ToArray();
    }
    private static PreparedSpell[] ValidateStartingSpells(string classId,string[]? selected,SpellDefinition[] catalog,
        string[] wizardSpellbook)
    {
        return SpellPreparation.Add([],new ClassLevel(classId,1),catalog,selected ?? [],wizardSpellbook);
    }
    private static Character Materialize(Guid id,Guid campaignId,string name,ProgressionState state,CharacterRules rules,
        CombatContent combat,HitPoints health,long revision)
    {
        var level = state.Classes.Sum(x => x.Level);
        var abilities = Enum.GetValues<Ability>().ToDictionary(a => a,a => state.BaseAbilities[a] +
            state.BackgroundBonuses.GetValueOrDefault(a) + state.AdvancementBonuses.GetValueOrDefault(a));
        // Derive the persisted Phase 1/2 projection from the same choice state used by the rich sheet.
        var preliminary = new Character(id,campaignId,name,level,abilities,[],[],10,health,revision);
        var sheet = CharacterDeriver.Derive(preliminary,state,rules,combat);
        return new Character(id,campaignId,name,level,abilities,
            sheet.Proficiencies.Where(x => x.Kind == ProficiencyKind.Skill).Select(x => x.Id),
            sheet.Proficiencies.Where(x => x.Kind == ProficiencyKind.Save).Select(x => Enum.Parse<Ability>(x.Id)),
            sheet.ArmorClass.Total,health,revision);
    }
    private static CombatProfile Profile(Guid id,CharacterSheet sheet,CombatContent combat,CombatProfile? old)
    {
        var weaponIds = sheet.Inventory.Where(x => combat.Weapons.Any(w => w.Id == x.DefinitionId))
            .Select(x => x.Id).ToHashSet();
        var weapons = old?.State.Weapons.Where(x => weaponIds.Contains(x.Id)).ToList() ?? [];
        foreach (var item in sheet.Inventory.Where(x => weaponIds.Contains(x.Id) && weapons.All(w => w.Id != x.Id)))
            weapons.Add(new(item.Id,item.DefinitionId,0));
        return new(new CombatProfileState(id,sheet.CombatCapabilities,weapons.ToArray(),old?.State.Conditions ?? []));
    }
    private CampaignEvent Event(Character character,string type,long revision,object data) =>
        new(0,Guid.NewGuid(),character.CampaignId,character.Id,type,clock.GetUtcNow(),Ruleset.Current,
            TimelineSerialization.SchemaVersion,revision,TimelineSerialization.Serialize(data));
}
