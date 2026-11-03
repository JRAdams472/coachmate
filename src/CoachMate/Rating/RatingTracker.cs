using System.Text.Json;

namespace CoachMate.Rating;

/// <summary>Glicko-2 rating for the user, updated per finished game vs engine levels.</summary>
public sealed class RatingTracker
{
    public const double Q = 0.005756462732485114; // ln(10)/400
    private const double Scale = 173.7178;
    public double R = 1500, Rd = 350, Sigma = 0.06;
    public int Games;

    private static readonly string Path_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoachMate", "rating.json");

    public static RatingTracker Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var t = JsonSerializer.Deserialize<RatingTracker>(File.ReadAllText(Path_));
                if (t is not null) return t;
            }
        }
        catch { }
        return new RatingTracker();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
        File.WriteAllText(Path_, JsonSerializer.Serialize(this));
    }

    /// <summary>Glicko-2 update for one result vs an opponent (score: 1 win, 0.5 draw, 0 loss).</summary>
    public void Apply(double oppR, double oppRd, double score)
    {
        var mu = (R - 1500) / Scale; var phi = Rd / Scale;
        var muJ = (oppR - 1500) / Scale; var phiJ = oppRd / Scale;
        var g = 1.0 / Math.Sqrt(1 + 3 * phiJ * phiJ / (Math.PI * Math.PI));
        var e = 1.0 / (1 + Math.Exp(-g * (mu - muJ)));
        var v = 1.0 / (g * g * e * (1 - e));
        var phiStar = Math.Sqrt(phi * phi + Sigma * Sigma);
        var phiNew = 1.0 / Math.Sqrt(1 / (phiStar * phiStar) + 1 / v);
        var muNew = mu + phiNew * g * (score - e);
        R = muNew * Scale + 1500;
        Rd = Math.Clamp(phiNew * Scale, 30, 350);
        Games++;
        Save();
    }

    /// <summary>Self-set estimate — resets to max uncertainty so finished games recalibrate fast.</summary>
    public void SetEstimate(double r)
    {
        R = Math.Clamp(r, 300, 2500);
        Rd = 350;
        Games = 0;
        Save();
    }

    public string Display => $"~{(int)Math.Round(R)} (±{(int)Math.Round(Rd)})";
}
