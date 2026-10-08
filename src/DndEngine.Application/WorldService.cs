using DndEngine.Domain;
using DndEngine.Domain.World;

namespace DndEngine.Application;

public sealed class WorldService(IWorldStore store, ICampaignStore campaigns, TimeProvider clock)
{
    private static readonly HashSet<string> EventTypes = [
        "NPCIntroduced","NPCStatusChanged","NPCMoved","NPCChanged","LocationCreated",
        "LocationChanged","FactionCreated","FactionChanged","FactionMembershipChanged",
        "RelationshipChanged","FactEstablished","FactChanged","KnowledgeAcquired",
        "SecretDisclosed","QuestCreated","QuestStarted","QuestProgressed",
        "QuestCompleted","QuestFailed","QuestChanged"];

    public async Task<DmWorldState> GetDmAsync(Guid campaignId,CancellationToken ct=default)
    {
        var campaign=await Campaign(campaignId,ct);
        return new(await store.GetAsync(campaignId,ct) ?? WorldState.Empty(campaignId),
            campaign.GameSeconds);
    }

    public async Task<PublicWorldState> GetPublicAsync(Guid campaignId,CancellationToken ct=default)
    {
        var view=await GetDmAsync(campaignId,ct);
        var s=view.State;
        var locations=s.Locations.Where(x=>x.IsDiscovered).ToArray();
        var locationIds=locations.Select(x=>x.Id).ToHashSet();
        var factions=s.Factions.Where(x=>x.IsPublic).ToArray();
        var factionIds=factions.Select(x=>x.Id).ToHashSet();
        var npcs=s.Npcs.Where(x=>x.IsPublic).ToArray();
        var npcIds=npcs.Select(x=>x.Id).ToHashSet();
        return new(s.CampaignId,s.Revision,view.GameSeconds,
            npcs.Select(x=>new PublicNpc(x.Id,x.Name,x.Description,x.Appearance,
                x.LocationId is { } id && locationIds.Contains(id) ? id : null,
                x.Status,x.PublicInformation)).ToArray(),
            locations.Select(x=>x with {
                ParentId=x.ParentId is { } p && locationIds.Contains(p) ? p : null,
                ControllingFactionId=x.ControllingFactionId is { } f && factionIds.Contains(f) ? f : null
            }).ToArray(),
            factions.Select(x=>new PublicFaction(x.Id,x.Name,x.Description,x.Status,
                x.LeaderNpcId is { } n && npcIds.Contains(n) ? n : null,
                x.HeadquartersLocationId is { } h && locationIds.Contains(h) ? h : null)).ToArray(),
            s.Facts.Where(x=>x.Visibility==WorldVisibility.Public)
                .Select(x=>new PublicFact(x.Id,x.Key,x.Value)).ToArray(),
            s.Quests.Where(x=>x.IsPublic && x.Status!=QuestStatus.Unknown)
                .Select(x=>new PublicQuest(x.Id,x.Title,x.Description,x.Status,x.Objectives,
                    (x.RelatedNpcIds ?? []).Where(npcIds.Contains).ToArray(),
                    (x.RelatedFactionIds ?? []).Where(factionIds.Contains).ToArray(),
                    (x.RelatedLocationIds ?? []).Where(locationIds.Contains).ToArray(),
                    x.DeadlineGameSeconds)).ToArray());
    }

    public async Task<KnowledgeRecord[]> GetKnowledgeAsync(Guid campaignId,
        WorldEntityKind holderKind,Guid holderId,CancellationToken ct=default)
    {
        var s=(await GetDmAsync(campaignId,ct)).State;
        await RequireRef(s,new(holderKind,holderId),ct);
        return s.Knowledge.Where(x=>x.Holder.Kind==holderKind && x.Holder.Id==holderId).ToArray();
    }

    public async Task<CampaignEvent[]> GetRecentEventsAsync(Guid campaignId,
        long after=0,int limit=100,CancellationToken ct=default)
    {
        await Campaign(campaignId,ct);
        if (after<0 || limit is < 1 or > 500) throw new RuleViolation("Invalid world event cursor or limit.");
        return await store.GetEventsAsync(campaignId,after,limit,EventTypes,true,ct);
    }

    public async Task<CampaignEvent[]> GetPublicTimelineAsync(Guid campaignId,
        long after=0,int limit=100,CancellationToken ct=default)
    {
        await Campaign(campaignId,ct);
        if (after<0 || limit is < 1 or > 500) throw new RuleViolation("Invalid event cursor or limit.");
        return await store.GetEventsAsync(campaignId,after,limit,EventTypes,false,ct);
    }

    public async Task<DmWorldState> ApplyAsync(Guid campaignId,ApplyWorldChanges request,
        CancellationToken ct=default)
    {
        var campaign=await Campaign(campaignId,ct);
        var before=await store.GetAsync(campaignId,ct) ?? WorldState.Empty(campaignId);
        if (before.Revision!=request.ExpectedRevision)
            throw new StateConflictException("World revision changed. Reload before applying consequences.");
        Required(request.Cause,"Cause",500);
        if (request.Changes is null || request.Changes.Length is < 1 or > 50)
            throw new RuleViolation("A world command requires 1–50 explicit changes.");
        var state=before;
        var events=new List<CampaignEvent>();
        foreach (var change in request.Changes)
        {
            if (change is null) throw new RuleViolation("World change cannot be null.");
            var (next,type,subjectBefore,subjectAfter)=Change(state,change);
            if (!EventTypes.Contains(type))
                throw new InvalidOperationException("Narrative event type lacks a visibility classification.");
            if (TimelineSerialization.Serialize(state).GetRawText()==
                TimelineSerialization.Serialize(next).GetRawText())
                throw new RuleViolation("World change has no effect.");
            await Validate(next,campaign,ct);
            state=next;
            events.Add(new(0,Guid.NewGuid(),campaignId,null,type,clock.GetUtcNow(),
                campaign.Ruleset,TimelineSerialization.SchemaVersion,null,
                TimelineSerialization.Serialize(new {
                    request.Cause,CampaignGameSeconds=campaign.GameSeconds,
                    WorldRevisionBefore=before.Revision,WorldRevisionAfter=before.Revision+1,
                    Before=subjectBefore,After=subjectAfter })));
        }
        state=state with { Revision=before.Revision+1 };
        await store.SaveAsync(before,state,campaign,events,ct);
        return new(state,campaign.GameSeconds);
    }

    private static (WorldState Next,string Type,object? Before,object After) Change(
        WorldState s,WorldChange c)
    {
        if (!Enum.IsDefined(c.Kind)) throw new RuleViolation("Unknown world change kind.");
        if (new object?[] { c.Npc,c.Location,c.Faction,c.Membership,c.Relationship,
            c.Fact,c.Knowledge,c.Quest }.Count(x=>x is not null)!=1)
            throw new RuleViolation("A world change must contain exactly one typed payload.");
        switch(c.Kind)
        {
            case WorldChangeKind.CreateNpc:
            {
                var x=Require(c.Npc); New(s.Npcs,x.Id);
                return (s with { Npcs=[..s.Npcs,x] },"NPCIntroduced",null,x);
            }
            case WorldChangeKind.UpdateNpc:
            {
                var x=Require(c.Npc); var old=Existing(s.Npcs,x.Id);
                var type=old.Status!=x.Status ? "NPCStatusChanged" :
                    old.LocationId!=x.LocationId ? "NPCMoved" : "NPCChanged";
                return (s with { Npcs=Replace(s.Npcs,x,x.Id) },type,old,x);
            }
            case WorldChangeKind.CreateLocation:
            {
                var x=Require(c.Location); New(s.Locations,x.Id);
                return (s with { Locations=[..s.Locations,x] },"LocationCreated",null,x);
            }
            case WorldChangeKind.UpdateLocation:
            {
                var x=Require(c.Location); var old=Existing(s.Locations,x.Id);
                return (s with { Locations=Replace(s.Locations,x,x.Id) },"LocationChanged",old,x);
            }
            case WorldChangeKind.CreateFaction:
            {
                var x=Require(c.Faction); New(s.Factions,x.Id);
                return (s with { Factions=[..s.Factions,x] },"FactionCreated",null,x);
            }
            case WorldChangeKind.UpdateFaction:
            {
                var x=Require(c.Faction); var old=Existing(s.Factions,x.Id);
                return (s with { Factions=Replace(s.Factions,x,x.Id) },"FactionChanged",old,x);
            }
            case WorldChangeKind.SetMembership:
            {
                var x=Require(c.Membership); var old=s.Memberships.SingleOrDefault(y=>y.Id==x.Id);
                if (old is null) New(s.Memberships,x.Id);
                else if (old.FactionId!=x.FactionId || old.Member!=x.Member)
                    throw new RuleViolation("Membership identity cannot change.");
                return (s with { Memberships=old is null ? [..s.Memberships,x] :
                    Replace(s.Memberships,x,x.Id) },"FactionMembershipChanged",old,x);
            }
            case WorldChangeKind.SetRelationship:
            {
                var x=Require(c.Relationship); var old=s.Relationships.SingleOrDefault(y=>y.Id==x.Id);
                if (old is not null && (old.From!=x.From || old.To!=x.To))
                    throw new RuleViolation("Relationship direction cannot change under the same ID.");
                return (s with { Relationships=old is null ? [..s.Relationships,x] :
                    Replace(s.Relationships,x,x.Id) },"RelationshipChanged",old,x);
            }
            case WorldChangeKind.EstablishFact:
            {
                var x=Require(c.Fact); New(s.Facts,x.Id);
                return (s with { Facts=[..s.Facts,x] },"FactEstablished",null,x);
            }
            case WorldChangeKind.UpdateFact:
            case WorldChangeKind.DiscloseSecret:
            {
                var x=Require(c.Fact); var old=Existing(s.Facts,x.Id);
                if (c.Kind==WorldChangeKind.UpdateFact &&
                    old.Visibility==WorldVisibility.Secret && x.Visibility==WorldVisibility.Public)
                    throw new RuleViolation("Use explicit secret disclosure to publish a secret.");
                if (c.Kind==WorldChangeKind.DiscloseSecret &&
                    (old.Visibility!=WorldVisibility.Secret || x.Visibility!=WorldVisibility.Public ||
                     old.Key!=x.Key || old.Value!=x.Value))
                    throw new RuleViolation("Secret disclosure must publish the unchanged secret fact.");
                return (s with { Facts=Replace(s.Facts,x,x.Id) },
                    c.Kind==WorldChangeKind.DiscloseSecret ? "SecretDisclosed" : "FactChanged",old,x);
            }
            case WorldChangeKind.GrantKnowledge:
            {
                var x=Require(c.Knowledge); var old=s.Knowledge.SingleOrDefault(y=>y.Id==x.Id);
                if (old is not null && (old.FactId!=x.FactId || old.Holder!=x.Holder))
                    throw new RuleViolation("Knowledge identity cannot change.");
                return (s with { Knowledge=old is null ? [..s.Knowledge,x] :
                    Replace(s.Knowledge,x,x.Id) },"KnowledgeAcquired",old,x);
            }
            case WorldChangeKind.CreateQuest:
            {
                var x=Require(c.Quest); New(s.Quests,x.Id);
                if (x.Status is not (QuestStatus.Unknown or QuestStatus.Discovered))
                    throw new RuleViolation("A new quest must start Unknown or Discovered.");
                return (s with { Quests=[..s.Quests,x] },"QuestCreated",null,x);
            }
            case WorldChangeKind.UpdateQuest:
            {
                var x=Require(c.Quest); var old=Existing(s.Quests,x.Id);
                if (x.Objectives is null) throw new RuleViolation("Quest objectives are required.");
                if (!Allowed(old.Status,x.Status)) throw new RuleViolation("Invalid quest status transition.");
                if (old.Objectives.Any(o=>!x.Objectives.Any(n=>n.Id==o.Id) ||
                    o.Completed && !x.Objectives.Single(n=>n.Id==o.Id).Completed))
                    throw new RuleViolation("Quest objectives cannot be removed or reset.");
                if (!ObjectivesEqual(old.Objectives,x.Objectives) && old.Status!=QuestStatus.Active)
                    throw new RuleViolation("Quest objectives can progress only while active.");
                var type=x.Status switch {
                    QuestStatus.Active when old.Status!=x.Status => "QuestStarted",
                    QuestStatus.Completed when old.Status!=x.Status => "QuestCompleted",
                    QuestStatus.Failed when old.Status!=x.Status => "QuestFailed",
                    _ => ObjectivesEqual(old.Objectives,x.Objectives) ? "QuestChanged" : "QuestProgressed" };
                return (s with { Quests=Replace(s.Quests,x,x.Id) },type,old,x);
            }
            default: throw new RuleViolation("Unsupported world change.");
        }
    }

    private async Task Validate(WorldState s,Campaign campaign,CancellationToken ct)
    {
        if (s.Npcs.Length>5000 || s.Locations.Length>5000 || s.Factions.Length>1000 ||
            s.Memberships.Length>10000 || s.Relationships.Length>20000 ||
            s.Facts.Length>10000 || s.Knowledge.Length>20000 || s.Quests.Length>2000)
            throw new RuleViolation("World aggregate exceeds its supported size.");
        Unique(s.Npcs.Select(x=>x.Id)); Unique(s.Locations.Select(x=>x.Id));
        Unique(s.Factions.Select(x=>x.Id)); Unique(s.Memberships.Select(x=>x.Id));
        Unique(s.Relationships.Select(x=>x.Id)); Unique(s.Facts.Select(x=>x.Id));
        Unique(s.Knowledge.Select(x=>x.Id)); Unique(s.Quests.Select(x=>x.Id));
        foreach (var n in s.Npcs)
        {
            Required(n.Name,"NPC name",200); Required(n.Description,"NPC description",2000);
            Optional(n.Appearance,"NPC appearance",2000);
            Optional(n.Background,"NPC background",2000);
            Optional(n.Personality,"NPC personality",2000);
            Optional(n.Motivations,"NPC motivations",2000);
            Optional(n.Fears,"NPC fears",2000); Optional(n.Goals,"NPC goals",2000);
            Optional(n.PublicInformation,"NPC public information",4000);
            Optional(n.PrivateInformation,"NPC private information",4000);
            if (!Enum.IsDefined(n.Status)) throw new RuleViolation("Invalid NPC status.");
            if (n.LocationId is { } location && !s.Locations.Any(x=>x.Id==location))
                throw new RuleViolation("NPC location is absent.");
            if (n.MechanicalCharacterId is { } mechanical)
                await RequireCharacter(mechanical,campaign.Id,ct);
        }
        foreach (var l in s.Locations)
        {
            Required(l.Name,"Location name",200); Required(l.Description,"Location description",2000);
            Optional(l.Condition,"Location condition",500);
            if (!Enum.IsDefined(l.Kind)) throw new RuleViolation("Invalid location type.");
            if (l.ParentId is { } parent)
            {
                var p=s.Locations.SingleOrDefault(x=>x.Id==parent)
                    ?? throw new RuleViolation("Parent location is absent.");
                if ((int)p.Kind+1!=(int)l.Kind) throw new RuleViolation("Location hierarchy must follow the supported levels.");
                var visited=new HashSet<Guid> { l.Id };
                while (p is not null)
                {
                    if (!visited.Add(p.Id)) throw new RuleViolation("Location parent cycle.");
                    p=p.ParentId is { } next ? s.Locations.SingleOrDefault(x=>x.Id==next) : null;
                }
            }
            else if (l.Kind!=LocationKind.World)
                throw new RuleViolation("Only a World location can have no parent.");
            if (l.ControllingFactionId is { } f && !s.Factions.Any(x=>x.Id==f))
                throw new RuleViolation("Location controlling faction is absent.");
        }
        foreach (var f in s.Factions)
        {
            Required(f.Name,"Faction name",200); Required(f.Description,"Faction description",2000);
            Optional(f.Goals,"Faction goals",2000); Optional(f.Motivations,"Faction motivations",2000);
            Optional(f.Status,"Faction status",200);
            if (f.Influence is < 0 or > 100) throw new RuleViolation("Faction influence must be 0–100.");
            if ((f.Resources ?? []).Any(x=>string.IsNullOrWhiteSpace(x.Key) ||
                x.Key.Length>100 || x.Value is < 0 or > 1_000_000))
                throw new RuleViolation("Faction resources require named nonnegative authored quantities.");
            if (f.LeaderNpcId is { } leader && !s.Npcs.Any(x=>x.Id==leader))
                throw new RuleViolation("Faction leader is absent.");
            if (f.HeadquartersLocationId is { } h && !s.Locations.Any(x=>x.Id==h))
                throw new RuleViolation("Faction headquarters is absent.");
            foreach (var ally in f.AlliedFactionIds ?? [])
                if (ally==f.Id || !s.Factions.Any(x=>x.Id==ally) ||
                    (f.EnemyFactionIds ?? []).Contains(ally))
                    throw new RuleViolation("Invalid faction ally.");
            foreach (var enemy in f.EnemyFactionIds ?? [])
                if (enemy==f.Id || !s.Factions.Any(x=>x.Id==enemy))
                    throw new RuleViolation("Invalid faction enemy.");
            Unique(f.AlliedFactionIds ?? []); Unique(f.EnemyFactionIds ?? []);
        }
        foreach (var m in s.Memberships)
        {
            if (!s.Factions.Any(x=>x.Id==m.FactionId)) throw new RuleViolation("Membership faction is absent.");
            await RequireRef(s,m.Member,ct);
            Required(m.Role,"Membership role",200);
        }
        Unique(s.Memberships.Where(x=>x.Active).Select(x=>$"{x.FactionId}:{x.Member.Kind}:{x.Member.Id}"));
        foreach (var r in s.Relationships)
        {
            await RequireRef(s,r.From,ct); await RequireRef(s,r.To,ct);
            if (r.From==r.To || new[] { r.Trust,r.Respect,r.Affection,r.Fear,r.Suspicion,r.Hostility }
                .Any(x=>x is < -5 or > 5)) throw new RuleViolation("Invalid relationship dimensions or direction.");
            Required(r.Label,"Relationship label",200);
            Optional(r.Note,"Relationship note",2000);
        }
        Unique(s.Relationships.Select(x=>$"{x.From.Kind}:{x.From.Id}>{x.To.Kind}:{x.To.Id}"));
        foreach (var f in s.Facts)
        {
            Required(f.Key,"Fact key",200); Required(f.Source,"Fact source",500);
            ValidateValue(f.Value);
            if (!Enum.IsDefined(f.Visibility)) throw new RuleViolation("Invalid fact visibility.");
        }
        Unique(s.Facts.Select(x=>x.Key));
        foreach (var k in s.Knowledge)
        {
            var fact=s.Facts.SingleOrDefault(x=>x.Id==k.FactId)
                ?? throw new RuleViolation("Knowledge fact is absent.");
            await RequireRef(s,k.Holder,ct);
            if (!Enum.IsDefined(k.Status) || k.Confidence is < 0 or > 100 ||
                k.AcquiredGameSeconds<0 || k.AcquiredGameSeconds>campaign.GameSeconds ||
                k.Status==KnowledgeStatus.Known && k.BeliefValue is not null && k.BeliefValue!=fact.Value)
                throw new RuleViolation("Invalid knowledge status, belief or campaign time.");
            if (k.BeliefValue is not null) ValidateValue(k.BeliefValue);
            Required(k.Source,"Knowledge source",500);
        }
        Unique(s.Knowledge.Select(x=>$"{x.FactId}:{x.Holder.Kind}:{x.Holder.Id}"));
        foreach (var q in s.Quests)
        {
            Required(q.Title,"Quest title",200); Required(q.Description,"Quest description",2000);
            if (!Enum.IsDefined(q.Status) || q.Objectives is null || q.Objectives.Length>100 ||
                q.DeadlineGameSeconds is < 0)
                throw new RuleViolation("Invalid quest structure.");
            Optional(q.Rewards,"Quest rewards",2000);
            Optional(q.Consequences,"Quest consequences",2000);
            if (q.Status is QuestStatus.Unknown or QuestStatus.Discovered &&
                q.Objectives.Any(x=>x.Completed))
                throw new RuleViolation("Quest objectives require an active quest.");
            Unique(q.Objectives.Select(x=>x.Id));
            foreach (var id in q.PrerequisiteQuestIds ?? [])
                if (id==q.Id || !s.Quests.Any(x=>x.Id==id))
                    throw new RuleViolation("Quest prerequisite is absent or self-referential.");
            Unique(q.PrerequisiteQuestIds ?? []);
            if (q.Status is QuestStatus.Active or QuestStatus.Completed &&
                (q.PrerequisiteQuestIds ?? []).Any(id=>s.Quests.Single(x=>x.Id==id).Status!=QuestStatus.Completed))
                throw new RuleViolation("Quest prerequisites must be completed first.");
            foreach (var id in q.RelatedNpcIds ?? [])
                if (!s.Npcs.Any(x=>x.Id==id)) throw new RuleViolation("Related NPC is absent.");
            Unique(q.RelatedNpcIds ?? []);
            foreach (var id in q.RelatedFactionIds ?? [])
                if (!s.Factions.Any(x=>x.Id==id)) throw new RuleViolation("Related faction is absent.");
            Unique(q.RelatedFactionIds ?? []);
            foreach (var id in q.RelatedLocationIds ?? [])
                if (!s.Locations.Any(x=>x.Id==id)) throw new RuleViolation("Related location is absent.");
            Unique(q.RelatedLocationIds ?? []);
            foreach (var o in q.Objectives)
            {
                Required(o.Description,"Objective description",500);
                Unique(o.DependsOnObjectiveIds ?? []);
                foreach (var id in o.DependsOnObjectiveIds ?? [])
                    if (id==o.Id || !q.Objectives.Any(x=>x.Id==id) ||
                        o.Completed && !q.Objectives.Single(x=>x.Id==id).Completed)
                        throw new RuleViolation("Objective dependency is invalid or incomplete.");
            }
            if (q.Status==QuestStatus.Completed && q.Objectives.Any(x=>!x.Completed))
                throw new RuleViolation("All objectives must be complete before completing the quest.");
            Acyclic(q.Objectives.Select(x=>x.Id),id=>q.Objectives.Single(x=>x.Id==id)
                .DependsOnObjectiveIds ?? []);
        }
        Unique(s.Quests.Select(x=>x.Title));
        Acyclic(s.Quests.Select(x=>x.Id),id=>s.Quests.Single(x=>x.Id==id)
            .PrerequisiteQuestIds ?? []);
    }

    private async Task RequireRef(WorldState state,WorldEntityRef reference,CancellationToken ct)
    {
        if (reference is null || reference.Id==Guid.Empty || !Enum.IsDefined(reference.Kind))
            throw new RuleViolation("Invalid world entity reference.");
        switch(reference.Kind)
        {
            case WorldEntityKind.Npc when state.Npcs.Any(x=>x.Id==reference.Id): return;
            case WorldEntityKind.Faction when state.Factions.Any(x=>x.Id==reference.Id): return;
            case WorldEntityKind.Character:
                await RequireCharacter(reference.Id,state.CampaignId,ct); return;
            default: throw new RuleViolation("World entity reference is absent.");
        }
    }
    private async Task RequireCharacter(Guid id,Guid campaignId,CancellationToken ct)
    {
        var character=await campaigns.GetCharacterAsync(id,ct);
        if (character is null || character.CampaignId!=campaignId)
            throw new RuleViolation("Mechanical character link is absent from this campaign.");
    }
    private async Task<Campaign> Campaign(Guid id,CancellationToken ct)
    {
        var campaign=await campaigns.GetCampaignAsync(id,ct)
            ?? throw new NotFoundException("Campaign not found.");
        campaign.Ruleset.RequireSupported(); return campaign;
    }
    private static bool Allowed(QuestStatus old,QuestStatus next) => old==next || old switch
    {
        QuestStatus.Unknown => next==QuestStatus.Discovered,
        QuestStatus.Discovered => next is QuestStatus.Active or QuestStatus.Abandoned,
        QuestStatus.Active => next is QuestStatus.Completed or QuestStatus.Failed or QuestStatus.Abandoned,
        _ => false
    };
    private static bool ObjectivesEqual(QuestObjective[] a,QuestObjective[] b) =>
        a.Length==b.Length && a.Zip(b).All(pair=>pair.First.Id==pair.Second.Id &&
            pair.First.Description==pair.Second.Description &&
            pair.First.Completed==pair.Second.Completed &&
            (pair.First.DependsOnObjectiveIds ?? []).SequenceEqual(
                pair.Second.DependsOnObjectiveIds ?? []));
    private static void ValidateValue(WorldValue value)
    {
        if (value is null) throw new RuleViolation("Fact value is required.");
        Required(value.Subject,"Fact subject",200);
        Required(value.Predicate,"Fact predicate",200);
        Required(value.Object,"Fact object",1000);
    }
    private static void Required(string? value,string label,int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length>maximum)
            throw new RuleViolation($"{label} must be nonempty and at most {maximum} characters.");
    }
    private static void Optional(string? value,string label,int maximum)
    {
        if (value is not null && value.Length>maximum)
            throw new RuleViolation($"{label} must be at most {maximum} characters.");
    }
    private static T Require<T>(T? value) where T:class =>
        value ?? throw new RuleViolation("World change payload is missing.");
    private static void Unique<T>(IEnumerable<T> values)
    {
        var array=values.ToArray();
        if (array.Any(x=>x is null || x is Guid id && id==Guid.Empty) ||
            array.Distinct().Count()!=array.Length)
            throw new RuleViolation("World identities or keys must be unique and nonempty.");
    }
    private static void Acyclic(IEnumerable<Guid> ids,Func<Guid,IEnumerable<Guid>> dependencies)
    {
        var visiting=new HashSet<Guid>(); var visited=new HashSet<Guid>();
        void Visit(Guid id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new RuleViolation("World dependency cycle.");
            foreach (var next in dependencies(id)) Visit(next);
            visiting.Remove(id); visited.Add(id);
        }
        foreach (var id in ids) Visit(id);
    }
    private static void New<T>(IEnumerable<T> values,Guid id) where T:class
    {
        if (id==Guid.Empty || values.Any(x=>Identity(x)==id))
            throw new RuleViolation("World entity identity already exists or is empty.");
    }
    private static T Existing<T>(IEnumerable<T> values,Guid id) where T:class =>
        values.SingleOrDefault(x=>Identity(x)==id)
            ?? throw new RuleViolation("World entity to update is absent.");
    private static T[] Replace<T>(IEnumerable<T> values,T replacement,Guid id) where T:class =>
        values.Select(x=>Identity(x)==id ? replacement : x).ToArray();
    private static Guid Identity<T>(T value) => value switch
    {
        NarrativeNpc x=>x.Id, WorldLocation x=>x.Id, WorldFaction x=>x.Id,
        FactionMembership x=>x.Id, WorldRelationship x=>x.Id,
        WorldFact x=>x.Id, KnowledgeRecord x=>x.Id, WorldQuest x=>x.Id,
        _=>throw new RuleViolation("Unsupported world entity identity.")
    };
}
