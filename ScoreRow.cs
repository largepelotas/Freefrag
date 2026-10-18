namespace Freefrag;

public sealed record ScoreRow(
    long Id, ulong SteamId, string PlayerName, string Map, string Route,
    int TrainingMode, int Difficulty, int BotWeapon, bool HeadshotOnly,
    int ElapsedMs, int Bots, int Deaths, int Kills, int HeadshotKills, DateTime FinishedAtUtc);
