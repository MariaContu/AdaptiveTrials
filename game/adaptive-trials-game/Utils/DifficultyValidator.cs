namespace AdaptiveTrials.Utils;

public static class DifficultyValidator
{
	public const int Minimum = 1;
	public const int Maximum = 3;

	public static bool IsValid(int difficulty)
	{
		return difficulty is >= Minimum and <= Maximum;
	}

	public static int Clamp(int difficulty)
	{
		return Math.Clamp(difficulty, Minimum, Maximum);
	}
}
