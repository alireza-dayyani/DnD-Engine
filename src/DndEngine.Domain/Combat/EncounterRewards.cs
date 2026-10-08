namespace DndEngine.Domain.Combat;

public enum EncounterOutcome { Victory, Defeat, Retreat, Other }

public sealed record ExperienceAward(Guid CharacterId,int Amount);
public sealed record EncounterRewardState(Guid EncounterId,EncounterOutcome Outcome,
    int AvailableExperience,ExperienceAward[] Awards,Guid[] DefeatedMonsterIds,
    long Revision=0);
