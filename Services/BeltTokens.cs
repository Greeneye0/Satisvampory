namespace Satisvampory.Services
{
    internal static class BeltTokens
    {
        // 1.0.115: a belt token must not be preceded by a letter. "Tailor1" is a name, not r1;
        // "R6S3S6" (glued after a space or a digit) and "Ore S2" still parse.
        public const string Receiver = @"(?<![a-z])r(\d+)";
        public const string Sender = @"(?<![a-z])s(\d+)";
    }
}
