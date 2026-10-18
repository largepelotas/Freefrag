namespace OpenPrefirePrac;

public static class PersonalBest
{
    public static bool IsBetter(int candidateMs, int? existingBestMs) =>
        existingBestMs == null || candidateMs < existingBestMs.Value;
}
