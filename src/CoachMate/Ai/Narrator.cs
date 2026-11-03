namespace CoachMate.Ai;

/// <summary>Builds grounded prompts: the model narrates facts the engine already computed.</summary>
public static class Narrator
{
    public const string System =
        "You are a chess coach explaining moves to a student. Use only the facts given — " +
        "the game move list, the per-move coach notes with grades and reasons, the engine evaluation, the best move, and refutation lines. Answer questions about any move in the game using those notes. " +
        "Write 2-3 plain sentences: what was wrong (or why the move was good), and what the student " +
        "should have seen. Never invent lines or evaluations not provided.";

    public static string Prompt(string fen, string san, string grade,
        int lossCp, string? bestSan, string? line, List<string> findings) => $"""
        Position (FEN): {fen}
        Student played: {san} — graded {grade}, giving up {lossCp / 100.0:0.0} pawns.
        Engine preferred: {bestSan ?? "unknown"}
        Engine line: {line ?? "n/a"}
        Coach findings: {(findings.Count > 0 ? string.Join(" | ", findings) : "none")}
        Explain in 2-3 sentences.
        """;
}
