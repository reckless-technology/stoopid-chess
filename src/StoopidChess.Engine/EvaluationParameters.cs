namespace StoopidChess.Engine;

/// <summary>
/// The tunable values of the evaluation, one instance per <see cref="Evaluator"/>, defaulting to the
/// constants in <see cref="Evaluator"/>. Every value can be read and set by name, which is how the UCI
/// options and the SPSA tuner in tools/spsa.py reach them; <see cref="Version"/> changes with every
/// set so that the evaluator knows to rebuild its tables and empty its cache.
/// </summary>
public sealed class EvaluationParameters
{
    private static readonly Dictionary<string, (Func<EvaluationParameters, int> Get, Action<EvaluationParameters, int> Set, int Min, int Max)> Accessors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KnightValue"] = (p => p.KnightValue, (p, v) => p.KnightValue = v, 200, 500),
        ["BishopValue"] = (p => p.BishopValue, (p, v) => p.BishopValue = v, 200, 500),
        ["RookValue"] = (p => p.RookValue, (p, v) => p.RookValue = v, 350, 750),
        ["QueenValue"] = (p => p.QueenValue, (p, v) => p.QueenValue = v, 700, 1400),
        ["KnightMobility"] = (p => p.KnightMobility, (p, v) => p.KnightMobility = v, 0, 20),
        ["BishopMobility"] = (p => p.BishopMobility, (p, v) => p.BishopMobility = v, 0, 20),
        ["RookMobility"] = (p => p.RookMobility, (p, v) => p.RookMobility = v, 0, 20),
        ["QueenMobility"] = (p => p.QueenMobility, (p, v) => p.QueenMobility = v, 0, 20),
        ["KingZoneAttackPenalty"] = (p => p.KingZoneAttackPenalty, (p, v) => p.KingZoneAttackPenalty = v, 0, 20),
        ["KingZoneQueenAttackPenalty"] = (p => p.KingZoneQueenAttackPenalty, (p, v) => p.KingZoneQueenAttackPenalty = v, 0, 30),
        ["WeakKingSquarePenalty"] = (p => p.WeakKingSquarePenalty, (p, v) => p.WeakKingSquarePenalty = v, 0, 50),
        ["BishopPairBonus"] = (p => p.BishopPairBonus, (p, v) => p.BishopPairBonus = v, 0, 120),
        ["TempoBonus"] = (p => p.TempoBonus, (p, v) => p.TempoBonus = v, 0, 40),
        ["KnightOutpostBonus"] = (p => p.KnightOutpostBonus, (p, v) => p.KnightOutpostBonus = v, 0, 60),
        ["ShelterRank2"] = (p => p.ShelterByRank[1], (p, v) => p.ShelterByRank[1] = v, 0, 60),
        ["ShelterRank3"] = (p => p.ShelterByRank[2], (p, v) => p.ShelterByRank[2] = v, 0, 60),
        ["ShelterRank4"] = (p => p.ShelterByRank[3], (p, v) => p.ShelterByRank[3] = v, 0, 60),
        ["ShelterMissing"] = (p => p.ShelterMissing, (p, v) => p.ShelterMissing = v, -80, 0),
        ["StormDistance2"] = (p => p.StormByDistance[2], (p, v) => p.StormByDistance[2] = v, 0, 60),
        ["StormDistance3"] = (p => p.StormByDistance[3], (p, v) => p.StormByDistance[3] = v, 0, 60),
        ["StormDistance4"] = (p => p.StormByDistance[4], (p, v) => p.StormByDistance[4] = v, 0, 60),
        ["PassedRank2"] = (p => p.PassedPawnBonus[1], (p, v) => p.PassedPawnBonus[1] = v, 0, 60),
        ["PassedRank3"] = (p => p.PassedPawnBonus[2], (p, v) => p.PassedPawnBonus[2] = v, 0, 80),
        ["PassedRank4"] = (p => p.PassedPawnBonus[3], (p, v) => p.PassedPawnBonus[3] = v, 0, 120),
        ["PassedRank5"] = (p => p.PassedPawnBonus[4], (p, v) => p.PassedPawnBonus[4] = v, 0, 160),
        ["PassedRank6"] = (p => p.PassedPawnBonus[5], (p, v) => p.PassedPawnBonus[5] = v, 0, 240),
        ["PassedRank7"] = (p => p.PassedPawnBonus[6], (p, v) => p.PassedPawnBonus[6] = v, 0, 320),
    };

    private static readonly EvaluationParameters Defaults = new();

    /// <summary>
    /// Incremented by every successful <see cref="TrySet"/>.
    /// </summary>
    public int Version { get; private set; }

    public int PawnValue { get; private set; } = Evaluator.PawnValue;
    public int KnightValue { get; private set; } = Evaluator.KnightValue;
    public int BishopValue { get; private set; } = Evaluator.BishopValue;
    public int RookValue { get; private set; } = Evaluator.RookValue;
    public int QueenValue { get; private set; } = Evaluator.QueenValue;
    public int KnightMobility { get; private set; } = Evaluator.KnightMobility;
    public int BishopMobility { get; private set; } = Evaluator.BishopMobility;
    public int RookMobility { get; private set; } = Evaluator.RookMobility;
    public int QueenMobility { get; private set; } = Evaluator.QueenMobility;
    public int KingZoneAttackPenalty { get; private set; } = Evaluator.KingZoneAttackPenalty;
    public int KingZoneQueenAttackPenalty { get; private set; } = Evaluator.KingZoneQueenAttackPenalty;
    public int WeakKingSquarePenalty { get; private set; } = Evaluator.WeakKingSquarePenalty;
    public int BishopPairBonus { get; private set; } = Evaluator.BishopPairBonus;
    public int TempoBonus { get; private set; } = Evaluator.TempoBonus;
    public int KnightOutpostBonus { get; private set; } = Evaluator.KnightOutpostBonus;
    public int ShelterMissing { get; private set; } = Evaluator.ShelterMissing;

    /// <summary>
    /// Pawn shelter by the pawn's rank from its own side; index 1 is the second rank.
    /// </summary>
    public int[] ShelterByRank { get; } = [.. Evaluator.DefaultShelterByRank];

    /// <summary>
    /// Pawn storm by the number of ranks between the king and the pawn.
    /// </summary>
    public int[] StormByDistance { get; } = [.. Evaluator.DefaultStormByDistance];

    /// <summary>
    /// Passed pawn bonus by rank from the pawn's own side; index 1 is the second rank.
    /// </summary>
    public int[] PassedPawnBonus { get; } = [.. Evaluator.DefaultPassedPawnBonus];

    /// <summary>
    /// The names every tunable value answers to.
    /// </summary>
    public static IEnumerable<string> Names => Accessors.Keys;

    /// <summary>
    /// The value of the named parameter, or null for an unknown name.
    /// </summary>
    public int? Get(string name) => Accessors.TryGetValue(name, out var accessor) ? accessor.Get(this) : null;

    /// <summary>
    /// The default and the allowed range of the named parameter, or null for an unknown name.
    /// </summary>
    public static (int Default, int Min, int Max)? Describe(string name)
    {
        return Accessors.TryGetValue(name, out var accessor) ? (accessor.Get(Defaults), accessor.Min, accessor.Max) : null;
    }

    /// <summary>
    /// Set the named parameter, clamped to its range. False for an unknown name.
    /// </summary>
    public bool TrySet(string name, int value)
    {
        if (!Accessors.TryGetValue(name, out var accessor))
        {
            return false;
        }
        accessor.Set(this, Math.Clamp(value, accessor.Min, accessor.Max));
        Version++;
        return true;
    }
}
